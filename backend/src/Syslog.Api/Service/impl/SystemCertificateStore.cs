using System.Net;
using System.Net.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Syslog.Api.Models;
using Syslog.Ingest.Models;
using Syslog.Ingest.Service;

namespace Syslog.Api.Service.impl;

/// <summary>管理 HTTPS 与接收 TLS 共用的证书来源，私钥只保存在服务账号可读的目录。</summary>
public sealed class SystemCertificateStore(string directory, TimeProvider? clock = null) : IListenerCertificateProvider, IDisposable
{
    public const int MaxUploadBytes = 256 * 1024;
    private const int MaxStoredBytes = 2 * MaxUploadBytes;
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const string AspNetDevelopmentCertificate = "1.3.6.1.4.1.311.84.1.1";
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateDirectoryMode = PrivateFileMode | UnixFileMode.UserExecute;
    private static readonly Pkcs12LoaderLimits LoaderLimits = new()
    {
        MaxKeys = 1,
        MaxCertificates = 16,
        MacIterationLimit = 300_000,
        IndividualKdfIterationLimit = 300_000,
        TotalKdfIterationLimit = 800_000
    };

    private readonly object _gate = new();
    private readonly string _directory = Path.GetFullPath(directory);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private ServerCertificateMaterial? _selfSigned;
    private StoredIdentity? _pending;
    private StoredIdentity? _current;
    private string? _startupError;
    private SslStreamCertificateContext? _activeHttpsContext;

    public bool CanUpload { get; set; } = true;
    public SslStreamCertificateContext? ActiveHttpsContext => Volatile.Read(ref _activeHttpsContext);

    public ServerCertificateMaterial PrepareUpload(byte[] bytes, string? password)
    {
        if (bytes.Length is 0 or > MaxUploadBytes)
            throw new CertificateValidationException("certificate_size_invalid", "请选择不超过 256 KiB 的证书文件。");
        if (password is { Length: > 1024 })
            throw new CertificateValidationException("certificate_invalid", "无法解包证书，请检查文件和密码。");
        return Parse(bytes, password);
    }

    public void Save(ServerCertificateMaterial candidate)
    {
        lock (_gate) SavePending(candidate);
    }

    private void SavePending(ServerCertificateMaterial candidate)
    {
        var identity = Identify(candidate);
        Write("uploaded.pfx", candidate);
        _pending = identity;
    }

    public void ActivateHttps(ServerCertificateMaterial candidate)
    {
        // 上下文持有独立叶证书。旧上下文交给正在握手的连接与 GC，避免释放仍在使用的私钥。
        var leaf = new X509Certificate2(candidate.Certificate);
        lock (_gate)
        {
            SslStreamCertificateContext context;
            try
            {
                context = SslStreamCertificateContext.Create(leaf, candidate.Chain, offline: true);
                SavePending(candidate);
            }
            catch
            {
                leaf.Dispose();
                throw;
            }
            SetCurrent(new(candidate, null, true));
            Volatile.Write(ref _activeHttpsContext, context);
        }
    }

    public void ObserveBootstrapCertificate(X509Certificate2 certificate)
    {
        lock (_gate)
            _current ??= new(Summarize(certificate), "");
    }

    public CertificateStartupResult LoadForStartup()
    {
        lock (_gate)
        {
            var uploaded = TryRead("uploaded.pfx", out var error);
            _pending = uploaded is null ? null : Identify(uploaded);
            if (uploaded is not null) return new(uploaded, null, true);
            var previous = TryRead("last-good.pfx", out var previousError);
            return new(previous, error ?? previousError, false);
        }
    }

    public void MarkStarted(CertificateStartupResult startup)
    {
        lock (_gate)
            if (_activeHttpsContext is null) SetCurrent(startup);
    }

    private void SetCurrent(CertificateStartupResult startup)
    {
        _startupError = startup.ErrorCode;
        if (startup.Certificate is null) return;
        _current = Identify(startup.Certificate);
        if (!startup.IsUploadedCandidate) return;

        try
        {
            // 证书已具备可用握手上下文或已成功启动，才更新私有回退副本。
            using var previous = TryRead("last-good.pfx", out _);
            if (previous is null || Identify(previous).MaterialHash != _current.MaterialHash)
                Write("last-good.pfx", startup.Certificate);
        }
        catch (CertificateValidationException)
        {
            _startupError = "certificate_backup_unavailable";
        }
    }

    public SystemCertificateView GetView()
    {
        lock (_gate)
        {
            var pending = _pending?.MaterialHash == _current?.MaterialHash ? null : _pending?.Summary;
            return new(_current?.Summary, pending, pending is not null, _startupError, CanUpload);
        }
    }

    public ServerCertificateMaterial LoadSelfSigned()
    {
        lock (_gate)
        {
            if (_selfSigned is not null) return Clone(_selfSigned);
            _selfSigned = TryRead("self-signed.pfx", out var error) ?? FindDevelopmentCertificate();
            if (_selfSigned is not null) return Clone(_selfSigned);
            if (error is not null && error != "certificate_expired") throw StoreUnavailable();

            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            names.AddIpAddress(IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(ServerAuthentication) }, false));
            var now = _clock.GetUtcNow();
            using var generated = new ServerCertificateMaterial(request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(2)));
            Write("self-signed.pfx", generated);
            _selfSigned = TryRead("self-signed.pfx", out _) ?? throw StoreUnavailable();
            return Clone(_selfSigned);
        }
    }

    public void SetSelfSigned(X509Certificate2 certificate)
    {
        lock (_gate)
        {
            var material = new ServerCertificateMaterial(new X509Certificate2(certificate));
            _selfSigned?.Dispose();
            _selfSigned = material;
        }
    }

    public IReadOnlyList<string> GetAvailableCertificateIds()
    {
        lock (_gate)
        {
            var ids = new List<string>(2);
            var now = _clock.GetUtcNow();
            if (_selfSigned is not null && _selfSigned.Certificate.NotAfter.ToUniversalTime() > now)
                ids.Add(IListenerCertificateProvider.SelfSignedId);
            if (_pending is { } pending && pending.Summary.NotAfter > now)
                ids.Add(IListenerCertificateProvider.UploadedId);
            return ids;
        }
    }

    public ServerCertificateMaterial? LoadCertificate(string id)
    {
        lock (_gate)
        {
            if (id == IListenerCertificateProvider.SelfSignedId && _selfSigned is not null)
            {
                var info = Summarize(_selfSigned.Certificate);
                return info.NotAfter > _clock.GetUtcNow() ? Clone(_selfSigned) : null;
            }
            return id == IListenerCertificateProvider.UploadedId ? TryRead("uploaded.pfx", out _) : null;
        }
    }

    public static CertificateSummary Summarize(X509Certificate2 certificate) =>
        new(certificate.GetCertHashString(HashAlgorithmName.SHA256), certificate.Subject,
            certificate.NotBefore.ToUniversalTime(), certificate.NotAfter.ToUniversalTime());

    private static StoredIdentity Identify(ServerCertificateMaterial material)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(material.Certificate.RawData);
        if (material.Chain is not null)
            foreach (var item in material.Chain) hash.AppendData(item.RawData);
        // 叶证书相同时仍按完整链辨别上传版本。
        return new(Summarize(material.Certificate), Convert.ToHexString(hash.GetHashAndReset()));
    }

    private ServerCertificateMaterial Parse(byte[] bytes, string? password)
    {
        X509Certificate2Collection certificates;
        try
        {
            certificates = X509CertificateLoader.LoadPkcs12Collection(bytes, password,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet, LoaderLimits);
        }
        catch (PlatformNotSupportedException) when (OperatingSystem.IsMacOS())
        {
            // 较早的 macOS 密钥提供程序只能使用临时钥匙串，不请求持久导入。
            try
            {
                certificates = X509CertificateLoader.LoadPkcs12Collection(bytes, password,
                    X509KeyStorageFlags.Exportable, LoaderLimits);
            }
            catch (CryptographicException) { throw InvalidCertificate(); }
        }
        catch (CryptographicException)
        {
            throw InvalidCertificate();
        }

        try
        {
            var leaf = certificates.FirstOrDefault(c => c.HasPrivateKey)
                ?? throw new CertificateValidationException("certificate_private_key_missing", "证书文件没有对应的私钥。");
            ValidateLeaf(leaf);
            var intermediates = GetIntermediates(leaf, certificates);
            foreach (var certificate in certificates)
                if (!ReferenceEquals(certificate, leaf) && !intermediates.Any(item => ReferenceEquals(item, certificate)))
                    certificate.Dispose();
            return new(leaf, intermediates);
        }
        catch (Exception error) when (error is CertificateValidationException or CryptographicException or ArgumentException)
        {
            foreach (var certificate in certificates) certificate.Dispose();
            if (error is CertificateValidationException) throw;
            throw InvalidCertificate();
        }
    }

    private void ValidateLeaf(X509Certificate2 certificate)
    {
        var now = _clock.GetUtcNow();
        if (now < certificate.NotBefore.ToUniversalTime() || now >= certificate.NotAfter.ToUniversalTime())
            throw new CertificateValidationException("certificate_expired", "证书已过期或尚未生效。");
        if (certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension => extension.CertificateAuthority))
            throw InvalidCertificate();
        using var rsa = certificate.GetRSAPrivateKey();
        using var ecdsa = certificate.GetECDsaPrivateKey();
        if (rsa is null && ecdsa is null) throw InvalidCertificate();
    }

    private X509Certificate2Collection GetIntermediates(X509Certificate2 leaf, X509Certificate2Collection certificates)
    {
        // 验证签发路径和服务器用途。上传时不访问网络，也不要求私有 CA 预装于服务账号信任库。
        using var chain = new X509Chain();
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.ChainPolicy.VerificationTime = _clock.GetUtcNow().UtcDateTime;
        chain.ChainPolicy.ApplicationPolicy.Add(new(ServerAuthentication));
        foreach (var certificate in certificates)
            if (!ReferenceEquals(certificate, leaf)) chain.ChainPolicy.ExtraStore.Add(certificate);
        if (!chain.Build(leaf)) throw InvalidCertificate();

        var result = new X509Certificate2Collection();
        foreach (var element in chain.ChainElements.Cast<X509ChainElement>().Skip(1))
        {
            var linked = certificates.FirstOrDefault(c => c.Equals(element.Certificate));
            if (linked is not null && !linked.SubjectName.RawData.AsSpan().SequenceEqual(linked.IssuerName.RawData))
                result.Add(linked);
        }
        return result;
    }

    private ServerCertificateMaterial? FindDevelopmentCertificate()
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var certificates = store.Certificates;
            try
            {
                var selected = certificates.Where(c => c.HasPrivateKey &&
                        c.Extensions[AspNetDevelopmentCertificate] is not null &&
                        c.SubjectName.RawData.AsSpan().SequenceEqual(c.IssuerName.RawData))
                    .OrderByDescending(c => c.NotAfter);
                foreach (var certificate in selected)
                {
                    try
                    {
                        ValidateLeaf(certificate);
                        if (!certificate.MatchesHostname("localhost", allowWildcards: true, allowCommonName: false)) continue;
                        return new(new X509Certificate2(certificate));
                    }
                    catch (CertificateValidationException) { }
                }
                return null;
            }
            finally
            {
                foreach (var certificate in certificates) certificate.Dispose();
            }
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private ServerCertificateMaterial? TryRead(string name, out string? error)
    {
        error = null;
        byte[]? bytes = null;
        try
        {
            CheckPrivatePath(_directory, isDirectory: true);
            var path = Path.Combine(_directory, name);
            CheckPrivatePath(path, isDirectory: false);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length is <= 0 or > MaxStoredBytes) throw StoreUnavailable();
            bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            return Parse(bytes, null);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (CertificateValidationException e)
        {
            error = e.Code;
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = "certificate_store_unavailable";
            return null;
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private void Write(string name, ServerCertificateMaterial material)
    {
        byte[]? bytes = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!Directory.Exists(_directory)) CreateWindowsDirectory(_directory);
            }
            else Directory.CreateDirectory(_directory, PrivateDirectoryMode);
            CheckPrivatePath(_directory, isDirectory: true);
            var path = Path.Combine(_directory, name);
            var stage = Path.Combine(_directory, "staged-" + Guid.NewGuid().ToString("N"));
            var contents = new X509Certificate2Collection(material.Certificate);
            if (material.Chain is { Count: > 0 } chain) contents.AddRange(chain);
            // 上传密码只用于解包。重新打包后由独占文件权限保护私钥。
            bytes = contents.Export(X509ContentType.Pkcs12) ?? throw StoreUnavailable();
            if (bytes.Length > MaxStoredBytes) throw StoreUnavailable();
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = PrivateFileMode;
            using (var file = new FileStream(stage, options))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            CheckPrivatePath(stage, isDirectory: false);
            File.Move(stage, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw StoreUnavailable();
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void CheckPrivatePath(string path, bool isDirectory)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            ((attributes & FileAttributes.Directory) != 0) != isDirectory) throw StoreUnavailable();
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = isDirectory ? (FileSystemSecurity)new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            var user = identity.User ?? throw StoreUnavailable();
            foreach (FileSystemAccessRule rule in rules)
                if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(user))
                    throw StoreUnavailable();
        }
        else
        {
            var allowed = isDirectory ? PrivateDirectoryMode : PrivateFileMode;
            if ((File.GetUnixFileMode(path) & ~allowed) != 0) throw StoreUnavailable();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateWindowsDirectory(string directory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is null) throw StoreUnavailable();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new(identity.User, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).Create(security);
    }

    private static ServerCertificateMaterial Clone(ServerCertificateMaterial source)
    {
        var chain = new X509Certificate2Collection();
        if (source.Chain is not null)
            foreach (var certificate in source.Chain) chain.Add(new X509Certificate2(certificate));
        return new(new X509Certificate2(source.Certificate), chain);
    }

    private static CertificateValidationException InvalidCertificate() =>
        new("certificate_invalid", "无法加载有效的服务器证书，请检查 PFX 文件、密码和证书链。");

    private static CertificateValidationException StoreUnavailable() =>
        new("certificate_store_unavailable", "证书存储不可用，请检查服务的私有证书目录。");

    private sealed record StoredIdentity(CertificateSummary Summary, string MaterialHash);

    public void Dispose()
    {
        lock (_gate)
        {
            _selfSigned?.Dispose();
            _selfSigned = null;
        }
    }
}
