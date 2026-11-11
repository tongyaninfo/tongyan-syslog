using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Syslog.Api.Models;
using Syslog.Api.Options;
using Syslog.Api.Service.impl;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class ControlStoreTests
{
    [Fact]
    public async Task SavedListenersOnlyApplyToANewStartupAndSurviveServiceRecreation()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var ticket = (await database.Store.LoginAsync("admin", Password, Ct)).Data!;
        var options = new IngestOptions();
        var service = ListenerService(database.Store, options);
        await service.InitializeAsync(Ct);
        var before = (await service.ReadAsync(Ct)).Data!;
        var draft = EnableProtocol(before.Saved.Configuration, "relp", 12514);
        var saved = await service.SaveAsync(new(before.Saved.Version, draft), admin.Id, ticket.Token, Ct);
        Assert.True(saved.Success);
        Assert.True(saved.Data!.RestartRequired);
        Assert.False(saved.Data.Running.Enabled);
        Assert.False(options.Enabled);
        await service.InitializeAsync(Ct);
        Assert.False(options.Enabled);

        var restartedOptions = new IngestOptions();
        var restarted = ListenerService(new ControlStore(database.Options, database.Clock), restartedOptions);
        await restarted.InitializeAsync(Ct);
        var after = (await restarted.ReadAsync(Ct)).Data!;
        Assert.False(after.RestartRequired);
        Assert.True(restartedOptions.Enabled);
        Assert.Equal(12514, restartedOptions.Listeners.Single(item => item.Transport == "relp").Port);
        Assert.True(restartedOptions.Listeners.Single(item => item.Transport == "relp").Enabled);
        Assert.False(restartedOptions.Listeners.Single(item => item.Transport == "udp").Enabled);
    }

    [Fact]
    public async Task CertificateReferencesDoNotLeakPathsOrPasswordsAndMissingCertificateCannotSave()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var ticket = (await database.Store.LoginAsync("admin", Password, Ct)).Data!;
        var options = new IngestOptions
        {
            TlsCertificates = new()
            {
                ["synthetic-cert"] = new() { CertificatePath = "private-synthetic-certificate.pfx", CertificatePassword = "synthetic-certificate-password" }
            }
        };
        var service = ListenerService(database.Store, options);
        await service.InitializeAsync(Ct);
        var draft = EnableProtocol(ListenerConfiguration.Default(), "tls", 16514);
        draft = draft with { Listeners = draft.Listeners.Select(item => item.Protocol == "tls" ? item with { CertificateId = "not-configured" } : item).ToArray() };
        var missing = await service.SaveAsync(new("0", draft), admin.Id, ticket.Token, Ct);
        Assert.Equal("listener_certificate_missing", missing.Code);
        Assert.Equal("0", (await service.ReadAsync(Ct)).Data!.Saved.Version);
        draft = draft with { Listeners = draft.Listeners.Select(item => item.Protocol == "tls" ? item with { CertificateId = "synthetic-cert" } : item).ToArray() };
        var saved = await service.SaveAsync(new("0", draft), admin.Id, ticket.Token, Ct);
        Assert.True(saved.Success);
        var json = JsonSerializer.Serialize(saved);
        Assert.DoesNotContain("private-synthetic-certificate", json);
        Assert.DoesNotContain("synthetic-certificate-password", json);
        Assert.Contains("synthetic-cert", json);

        var restarted = ListenerService(database.Store, options);
        await restarted.InitializeAsync(Ct);
        var tls = options.Listeners.Single(item => item.Transport == "tls");
        Assert.Equal("private-synthetic-certificate.pfx", tls.CertificatePath);
        Assert.Equal("synthetic-certificate-password", tls.CertificatePassword);
    }

    [Fact]
    public async Task ManagedReferencesKeepSelfSignedChoiceAfterUploadAndPersistOnlyTheSelectedId()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var ticket = (await database.Store.LoginAsync("admin", Password, Ct)).Data!;
        var options = new IngestOptions
        {
            TlsCertificates = new()
            {
                ["deployed"] = new() { CertificatePath = "synthetic-deployment-reference.pfx" }
            }
        };
        var certificates = new TestCertificateProvider();
        var service = ListenerService(database.Store, options, certificateProvider: certificates);
        await service.InitializeAsync(Ct);
        var before = (await service.ReadAsync(Ct)).Data!;
        Assert.Equal(["deployed", IListenerCertificateProvider.SelfSignedId], before.Certificates.Select(item => item.Id));

        var draft = EnableProtocol(before.Saved.Configuration, "tls", 16514);
        draft = SelectTlsCertificate(draft, IListenerCertificateProvider.UploadedId);
        var missing = await service.SaveAsync(new(before.Saved.Version, draft), admin.Id, ticket.Token, Ct);
        Assert.Equal("listener_certificate_missing", missing.Code);

        certificates.Uploaded = true;
        var saved = await service.SaveAsync(new(before.Saved.Version, draft), admin.Id, ticket.Token, Ct);
        Assert.True(saved.Success);
        Assert.Equal(["deployed", IListenerCertificateProvider.SelfSignedId, IListenerCertificateProvider.UploadedId],
            saved.Data!.Certificates.Select(item => item.Id));

        var restarted = ListenerService(database.Store, options, certificateProvider: certificates);
        await restarted.InitializeAsync(Ct);
        var tls = options.Listeners.Single(item => item.Transport == "tls");
        Assert.Equal(IListenerCertificateProvider.UploadedId, tls.CertificateId);
        Assert.Empty(tls.CertificatePath);
        Assert.Empty(tls.CertificatePassword);

        var selfSigned = await restarted.SaveAsync(new(saved.Data.Saved.Version,
            SelectTlsCertificate(draft, IListenerCertificateProvider.SelfSignedId)), admin.Id, ticket.Token, Ct);
        Assert.True(selfSigned.Success);
    }

    [Fact]
    public async Task EmergencyDisableOverridesSavedEnabledConfiguration()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var ticket = (await database.Store.LoginAsync("admin", Password, Ct)).Data!;
        Assert.True((await database.Store.SaveListenerSettingsAsync(new("0", EnableProtocol(ListenerConfiguration.Default(), "udp", 15514)), admin.Id, ticket.Token, Ct)).Success);
        var options = new IngestOptions();
        var service = ListenerService(database.Store, options, forceDisabled: true);
        await service.InitializeAsync(Ct);
        var view = (await service.ReadAsync(Ct)).Data!;
        Assert.False(options.Enabled);
        Assert.False(view.Running.Enabled);
        Assert.True(view.Saved.Configuration.Enabled);
        Assert.All(view.States, state => Assert.Equal("listener_forced_disabled", state.FailureCode));
    }

    [Fact]
    public async Task StartupOnMissingDatabaseDoesNotCreateOrInitializeIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "syslog-listeners-absent-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var store = new ControlStore(new ControlStoreOptions { ConnectionString = $"Data Source={path};Mode=ReadWrite" });
        var options = new IngestOptions { Enabled = true };
        var service = ListenerService(store, options);
        await service.InitializeAsync(Ct);
        Assert.False(options.Enabled);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task LegacyMultipleListenersKeepTheirRuntimeConfigurationAndCannotBeSilentlyReducedByTheEditor()
    {
        await using var database = await TestDatabase.OpenAsync();
        await database.BootstrapAsync();
        var options = new IngestOptions
        {
            Enabled = true,
            Listeners = Enumerable.Range(0, 32).Select(index => new ListenerOptions
                { Transport = "tcp", Address = "127.0.0.1", Port = 16000 + index }).ToList()
        };
        var service = ListenerService(database.Store, options);
        await service.InitializeAsync(Ct);
        Assert.True(options.Enabled);
        Assert.Equal(32, options.Listeners.Count);
        Assert.Equal(Enumerable.Range(16000, 32), options.Listeners.Select(item => item.Port));
        Assert.Equal("legacy_listener_configuration", (await service.ReadAsync(Ct)).Code);
        Assert.Equal("0", (await database.Store.ReadListenerSettingsAsync(Ct)).Data!.Version);
    }

    private static ListenerConfigurationService ListenerService(ControlStore store, IngestOptions options, bool forceDisabled = false,
        IListenerCertificateProvider? certificateProvider = null) =>
        new(store, Microsoft.Extensions.Options.Options.Create(options), new IngestStatus(), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ingest:ForceDisabled"] = forceDisabled.ToString() }).Build(),
            new RecordingReceptionRuntime(), certificateProvider);

    private static ListenerConfiguration EnableProtocol(ListenerConfiguration configuration, string protocol, int port) =>
        configuration with { Enabled = true, Listeners = configuration.Listeners.Select(item => item.Protocol == protocol ? item with { Enabled = true, Port = port } : item).ToArray() };

    private static ListenerConfiguration SelectTlsCertificate(ListenerConfiguration configuration, string id) =>
        configuration with { Listeners = configuration.Listeners.Select(item => item.Protocol == "tls" ? item with { CertificateId = id } : item).ToArray() };

    private sealed class TestCertificateProvider : IListenerCertificateProvider
    {
        public bool Uploaded { get; set; }
        public IReadOnlyList<string> GetAvailableCertificateIds() => Uploaded
            ? [IListenerCertificateProvider.SelfSignedId, IListenerCertificateProvider.UploadedId]
            : [IListenerCertificateProvider.SelfSignedId];

        public ServerCertificateMaterial? LoadCertificate(string id) => throw new InvalidOperationException("读取与保存配置不应加载证书私钥。");
    }
}
