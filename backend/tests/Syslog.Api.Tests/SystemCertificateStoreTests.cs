using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Syslog.Api.Models;
using Syslog.Api.Service.impl;
using Syslog.Ingest.Service;
using Xunit;

namespace Syslog.Api.Tests;

public sealed class SystemCertificateStoreTests
{
    private const string Host = "syslog.example.com";
    private const string Password = "synthetic-test-password";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T08:00:00Z");

    [Fact]
    public void UploadChecksPasswordPrivateKeyAndValidity()
    {
        using var store = new SystemCertificateStore(NewDirectory(), new FixedClock(Now));
        using var certificate = SelfSigned(Host, Now.AddDays(-1), Now.AddDays(5));
        var pfx = certificate.Export(X509ContentType.Pkcs12, Password);
        Assert.Equal("certificate_invalid",
            Assert.Throws<CertificateValidationException>(() => store.PrepareUpload(pfx, "wrong")).Code);

        using var expired = SelfSigned(Host, Now.AddDays(-5), Now.AddDays(-1));
        Assert.Equal("certificate_expired",
            Assert.Throws<CertificateValidationException>(() => store.PrepareUpload(expired.Export(X509ContentType.Pkcs12), null)).Code);
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        Assert.Equal("certificate_private_key_missing",
            Assert.Throws<CertificateValidationException>(() => store.PrepareUpload(publicOnly.Export(X509ContentType.Pkcs12), null)).Code);
        Assert.False(store.GetView().RestartRequired);
    }

    [Fact]
    public void UploadedCertificateKeepsItsIntermediateAndUsesIndependentTlsMaterial()
    {
        var directory = NewDirectory();
        using var store = new SystemCertificateStore(directory, new FixedClock(Now));
        using var selfSigned = SelfSigned("localhost", Now.AddDays(-1), Now.AddDays(10));
        store.SetSelfSigned(selfSigned);
        Assert.Equal([IListenerCertificateProvider.SelfSignedId], store.GetAvailableCertificateIds());

        using var rootKey = RSA.Create(2048);
        using var root = Authority("CN=Test Root", rootKey).CreateSelfSigned(Now.AddDays(-5), Now.AddDays(10));
        using var intermediateKey = RSA.Create(2048);
        using var intermediatePublic = Authority("CN=Test Intermediate", intermediateKey)
            .Create(root, Now.AddDays(-4), Now.AddDays(9), [1]);
        using var intermediate = intermediatePublic.CopyWithPrivateKey(intermediateKey);
        using var leafKey = RSA.Create(2048);
        using var leafPublic = Leaf(Host, leafKey).Create(intermediate, Now.AddDays(-3), Now.AddDays(8), [2]);
        using var leaf = leafPublic.CopyWithPrivateKey(leafKey);
        using var rootPublic = X509CertificateLoader.LoadCertificate(root.RawData);
        var input = new X509Certificate2Collection { leaf, intermediatePublic, rootPublic };
        using var candidate = store.PrepareUpload(input.Export(X509ContentType.Pkcs12, Password)!, Password);
        store.Save(candidate);
        Assert.Equal(SystemCertificateStore.Summarize(leaf).Fingerprint, store.GetView().Pending?.Fingerprint);
        Assert.Equal([IListenerCertificateProvider.SelfSignedId, IListenerCertificateProvider.UploadedId], store.GetAvailableCertificateIds());

        using var selfSignedForTls = store.LoadCertificate(IListenerCertificateProvider.SelfSignedId);
        Assert.True(selfSignedForTls!.Certificate.HasPrivateKey);
        using var uploadedForTls = store.LoadCertificate(IListenerCertificateProvider.UploadedId);
        Assert.True(uploadedForTls!.Certificate.HasPrivateKey);
        Assert.Equal(intermediatePublic.Thumbprint, Assert.Single(uploadedForTls.Chain!).Thumbprint);

        var startup = store.LoadForStartup();
        using var started = startup.Certificate;
        store.MarkStarted(startup);
        using var changedChain = store.PrepareUpload(leaf.Export(X509ContentType.Pkcs12, Password), Password);
        Assert.Empty(changedChain.Chain);
        store.Save(changedChain);
        Assert.True(store.GetView().RestartRequired);
        Assert.Equal(store.GetView().Current?.Fingerprint, store.GetView().Pending?.Fingerprint);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, "uploaded.pfx")));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
        }
    }

    [Fact]
    public void ConsecutivePendingUploadsDoNotOverwriteLastStartedCertificate()
    {
        var directory = NewDirectory();
        var clock = new FixedClock(Now);
        using var store = new SystemCertificateStore(directory, clock);
        using var first = SelfSigned(Host, Now.AddDays(-1), Now.AddDays(5));
        using var second = SelfSigned(Host, Now.AddDays(-1), Now.AddDays(5));
        using var third = SelfSigned(Host, Now.AddDays(-1), Now.AddDays(5));

        Save(store, first);
        var started = store.LoadForStartup();
        using var startedMaterial = started.Certificate;
        Assert.True(started.IsUploadedCandidate);
        store.MarkStarted(started);
        Assert.False(store.GetView().RestartRequired);

        Save(store, second);
        Save(store, third);
        Assert.Equal(SystemCertificateStore.Summarize(first).Fingerprint, store.GetView().Current?.Fingerprint);
        Assert.Equal(SystemCertificateStore.Summarize(third).Fingerprint, store.GetView().Pending?.Fingerprint);

        // 此处只破坏隔离测试生成的候选文件，模拟启动时读取损坏文件。
        File.WriteAllBytes(Path.Combine(directory, "uploaded.pfx"), [1, 2, 3]);
        using var restarted = new SystemCertificateStore(directory, clock);
        var fallback = restarted.LoadForStartup();
        using var fallbackMaterial = fallback.Certificate;
        Assert.False(fallback.IsUploadedCandidate);
        Assert.Equal("certificate_invalid", fallback.ErrorCode);
        restarted.MarkStarted(fallback);
        Assert.Equal(SystemCertificateStore.Summarize(first).Fingerprint, restarted.GetView().Current?.Fingerprint);
        Assert.Equal("certificate_invalid", restarted.GetView().LoadError);
    }

    private static void Save(SystemCertificateStore store, X509Certificate2 certificate)
    {
        using var material = store.PrepareUpload(certificate.Export(X509ContentType.Pkcs12), null);
        store.Save(material);
    }

    private static CertificateRequest Authority(string subject, RSA key)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request;
    }

    private static CertificateRequest Leaf(string hostname, RSA key)
    {
        var request = new CertificateRequest("CN=" + hostname, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(hostname);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        return request;
    }

    private static X509Certificate2 SelfSigned(string hostname, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        return Leaf(hostname, key).CreateSelfSigned(notBefore, notAfter);
    }

    private static string NewDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "TestResults", "certificate-store-" + Guid.NewGuid().ToString("N"));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
