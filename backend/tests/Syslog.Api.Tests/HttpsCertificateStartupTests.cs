using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Syslog.Api.Common;
using Syslog.Api.Service.impl;
using Syslog.Ingest.Service;
using Xunit;

namespace Syslog.Api.Tests;

public sealed class HttpsCertificateStartupTests
{
    [Fact]
    public async Task Kestrel_uses_staged_certificate_only_on_restart_and_falls_back_to_last_good()
    {
        using var original = CreateCertificate("original");
        using var candidate = CreateCertificate("candidate");
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "https-startup-" + Guid.NewGuid().ToString("N"));
        var originalFingerprint = SystemCertificateStore.Summarize(original).Fingerprint;
        var candidateFingerprint = SystemCertificateStore.Summarize(candidate).Fingerprint;

        await using (var app = await StartAsync(directory, original))
        {
            await AssertFingerprintAsync(app, originalFingerprint);
            var store = app.Services.GetRequiredService<SystemCertificateStore>();
            Assert.Equal([IListenerCertificateProvider.SelfSignedId], store.GetAvailableCertificateIds());
            var bytes = candidate.Export(X509ContentType.Pkcs12, "synthetic");
            try
            {
                using var upload = store.PrepareUpload(bytes, "synthetic");
                store.Save(upload);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            Assert.True(store.GetView().RestartRequired);
            Assert.Equal(candidateFingerprint, store.GetView().Pending?.Fingerprint);
            Assert.Contains(IListenerCertificateProvider.UploadedId, store.GetAvailableCertificateIds());
            await AssertFingerprintAsync(app, originalFingerprint);
        }

        await using (var app = await StartAsync(directory, original))
        {
            await AssertFingerprintAsync(app, candidateFingerprint);
            var store = app.Services.GetRequiredService<SystemCertificateStore>();
            Assert.Equal(candidateFingerprint, store.GetView().Current?.Fingerprint);
            Assert.False(store.GetView().RestartRequired);
            Assert.Equal([IListenerCertificateProvider.SelfSignedId, IListenerCertificateProvider.UploadedId], store.GetAvailableCertificateIds());
        }

        // 仅破坏测试目录中的候选副本，核验真实 TLS 握手是否使用上一份成功启动的证书。
        File.WriteAllBytes(Path.Combine(directory, "uploaded.pfx"), [0, 1, 2]);
        await using (var app = await StartAsync(directory, original))
        {
            await AssertFingerprintAsync(app, candidateFingerprint);
            Assert.NotNull(app.Services.GetRequiredService<SystemCertificateStore>().GetView().LoadError);
        }
    }

    [Fact]
    public async Task Mounted_default_certificate_allows_live_rotation_and_restart_uses_the_upload()
    {
        using var bootstrap = CreateCertificate("mounted-default");
        using var selfSigned = CreateCertificate("system-self-signed");
        using var upload = CreateCertificate("uploaded");
        var fixture = Path.Combine(AppContext.BaseDirectory, "TestResults", "https-mounted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var bootstrapPath = Path.Combine(fixture, "mounted.pfx");
        var directory = Path.Combine(fixture, "data", "https-certificate");
        const string password = "synthetic-bootstrap";
        File.WriteAllBytes(bootstrapPath, bootstrap.Export(X509ContentType.Pkcs12, password));
        var original = SystemCertificateStore.Summarize(bootstrap).Fingerprint;
        var updated = SystemCertificateStore.Summarize(upload).Fingerprint;

        await using (var app = await StartAsync(directory, selfSigned, bootstrapPath, password))
        {
            await AssertFingerprintAsync(app, original);
            var store = app.Services.GetRequiredService<SystemCertificateStore>();
            Assert.True(store.GetView().CanUpload);
            Assert.Equal(original, store.GetView().Current?.Fingerprint);
            using (var candidate = store.PrepareUpload(upload.Export(X509ContentType.Pkcs12), null))
                store.ActivateHttps(candidate);
            // 上传请求中的证书已经释放，同一 Kestrel 实例仍能进行新的握手。
            await AssertFingerprintAsync(app, updated);
            Assert.Equal(updated, store.GetView().Current?.Fingerprint);
            Assert.Null(store.GetView().Pending);
            Assert.False(store.GetView().RestartRequired);
        }

        await using (var app = await StartAsync(directory, selfSigned, bootstrapPath, password))
            await AssertFingerprintAsync(app, updated);

        File.WriteAllBytes(Path.Combine(directory, "uploaded.pfx"), [1, 2, 3]);
        await using (var app = await StartAsync(directory, selfSigned, bootstrapPath, password))
        {
            await AssertFingerprintAsync(app, updated);
            Assert.Equal("certificate_invalid", app.Services.GetRequiredService<SystemCertificateStore>().GetView().LoadError);
        }
    }

    [Theory]
    [InlineData("Kestrel:Endpoints:Admin:Certificate:Path", "host.pfx")]
    [InlineData("Kestrel:Endpoints:Admin:Sni:localhost:Certificate:Path", "host.pfx")]
    [InlineData("Kestrel:Endpoints:Admin:Protocols", "Http1AndHttp2AndHttp3")]
    [InlineData("Kestrel:EndpointDefaults:Protocols", "Http3")]
    public void Explicit_endpoint_certificate_and_unsupported_handshake_modes_remain_managed_by_kestrel(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        Assert.True(SystemHttpsStartup.HasExternalCertificate(configuration));
    }

    private static async Task<WebApplication> StartAsync(string directory, X509Certificate2 selfSigned,
        string? bootstrapPath = null, string? password = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        if (bootstrapPath is not null)
        {
            builder.Configuration["Kestrel:Certificates:Default:Path"] = bootstrapPath;
            builder.Configuration["Kestrel:Certificates:Default:Password"] = password;
            builder.WebHost.UseUrls("https://127.0.0.1:0");
        }
        builder.Services.AddSingleton(_ =>
        {
            var store = new SystemCertificateStore(directory)
            {
                CanUpload = !SystemHttpsStartup.HasExternalCertificate(builder.Configuration)
            };
            store.SetSelfSigned(selfSigned);
            return store;
        });
        builder.Services.AddSingleton<SystemHttpsStartup>();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ConfigureHttpsDefaults(https => options.ApplicationServices.GetRequiredService<SystemHttpsStartup>().Apply(https));
            if (bootstrapPath is null) options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps());
        });
        var app = builder.Build();
        var startup = app.Services.GetRequiredService<SystemHttpsStartup>();
        startup.InitializeListenerCertificates();
        app.Lifetime.ApplicationStarted.Register(startup.MarkStarted);
        try
        {
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static async Task AssertFingerprintAsync(WebApplication app, string expected)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var address = new Uri(Assert.Single(addresses));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, address.Port, timeout.Token);
        using var tls = new SslStream(socket.GetStream(), false, (_, cert, _, errors) =>
            (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0 && cert?.GetCertHashString(HashAlgorithmName.SHA256) == expected);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost" }, timeout.Token);
        Assert.True(tls.IsAuthenticated);
    }

    internal static X509Certificate2 CreateCertificate(string label, string hostname = "localhost")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={hostname},OU={label}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(hostname);
        request.CertificateExtensions.Add(names.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(3));
    }
}
