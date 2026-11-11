using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syslog.Api.Common;
using Syslog.Api.Middleware;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Api.Tests;

/// <summary>TestServer 在进程内调用 HTTP 管线，不绑定本机端口，也不创建数据库。</summary>
public sealed class ApiContractTests
{
    private const string PasswordChangePath = "/api/admin/v1/session/password";
    private static readonly ChangePasswordRequest PasswordChange = new("synthetic-password", "synthetic-new-password");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Https_certificate_upload_requires_authentication_and_committed_audit_but_no_matching_host(bool includeLegacyHost)
    {
        const string route = "/api/admin/v1/system/https-certificate";
        const string password = "synthetic-pfx-password";
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "https-upload-" + Guid.NewGuid().ToString("N"));
        using var store = new SystemCertificateStore(directory);
        using var app = await Harness.StartAsync(store);
        using var certificate = HttpsCertificateStartupTests.CreateCertificate("upload", "certificate.example.test");
        var bytes = certificate.Export(X509ContentType.Pkcs12, password);
        try
        {
            using (var anonymous = await SendAsync()) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            app.Authenticate();
            using (var denied = await SendAsync()) Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
            Assert.Null(store.GetView().Pending);
            Assert.Null(store.ActiveHttpsContext);
            app.Control.AllowCertificateAudit = true;
            using var accepted = await SendAsync();
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var response = await accepted.Content.ReadAsStringAsync();
            var fingerprint = SystemCertificateStore.Summarize(certificate).Fingerprint;
            Assert.Contains(fingerprint, response);
            Assert.DoesNotContain(password, response);
            Assert.DoesNotContain("synthetic.pfx", response);
            Assert.Equal(fingerprint, app.Control.AuditedCertificate?.Fingerprint);
            Assert.Equal(fingerprint, store.GetView().Current?.Fingerprint);
            Assert.False(store.GetView().RestartRequired);
            Assert.Null(store.GetView().Pending);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }

        async Task<HttpResponseMessage> SendAsync()
        {
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(bytes), "file", "synthetic.pfx");
            content.Add(new StringContent(password), "password");
            if (includeLegacyHost) content.Add(new StringContent("other.example.test"), "host");
            using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = content };
            request.Headers.Add("X-Syslog-Request", "1");
            request.Headers.Add("Origin", "https://example.invalid");
            return await app.Client.SendAsync(request);
        }
    }

    [Fact]
    public async Task AnonymousCannotReadLogsSourcesOrStatus()
    {
        using var app = await Harness.StartAsync();
        foreach (var path in new[] { "/api/admin/v1/sources", "/api/admin/v1/system/status", "/api/admin/v1/logs/1?receivedAt=2026-11-10T00:00:00Z" })
        {
            var response = await app.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("authentication_required", await CodeAsync(response));
        }
        Assert.Equal(0, app.Logs.Calls);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "https://attacker.invalid")]
    public async Task LoginRejectsMissingHeaderOrCrossOrigin(bool header, string? origin)
    {
        using var app = await Harness.StartAsync();
        if (header) app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        if (origin is not null) app.Client.DefaultRequestHeaders.Add("Origin", origin);
        var response = await app.Client.PostAsJsonAsync("/api/admin/v1/session/login", new { username = "operator", password = "synthetic-password" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_rejected", await CodeAsync(response));
        Assert.Equal(0, app.Control.LoginCalls);
    }

    [Fact]
    public async Task LoginUsesSecureCookieAndDoesNotExposeTokenInJson()
    {
        using var app = await Harness.StartAsync();
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        var response = await app.Client.PostAsJsonAsync("/api/admin/v1/session/login", new { username = "operator", password = "synthetic-password" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-syslog=", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeControl.Token, body);
        Assert.Contains("\"id\":\"9007199254740993\"", body);
        app.Authenticate();
        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync("/api/admin/v1/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client.PostAsync("/api/admin/v1/session/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.GetAsync("/api/admin/v1/session")).StatusCode);
    }

    [Fact]
    public async Task HttpLoginIsRejectedBeforeCredentialVerification()
    {
        using var app = await Harness.StartAsync();
        app.Client.BaseAddress = new Uri("http://localhost");
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        var response = await app.Client.PostAsJsonAsync("/api/admin/v1/session/login", new { username = "operator", password = "synthetic-password" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("https_required", await CodeAsync(response));
        Assert.Equal(0, app.Control.LoginCalls);
    }

    [Fact]
    public async Task PasswordChangeRequiresAuthenticationAndSameOriginProtection()
    {
        using var app = await Harness.StartAsync();
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.PostAsJsonAsync(PasswordChangePath, PasswordChange)).StatusCode);

        app.Authenticate();
        app.Client.DefaultRequestHeaders.Remove("X-Syslog-Request");
        var missingHeader = await app.Client.PostAsJsonAsync(PasswordChangePath, PasswordChange);
        Assert.Equal(HttpStatusCode.Forbidden, missingHeader.StatusCode);
        Assert.Equal("csrf_rejected", await CodeAsync(missingHeader));

        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        app.Client.DefaultRequestHeaders.Add("Origin", "https://other.invalid");
        var crossOrigin = await app.Client.PostAsJsonAsync(PasswordChangePath, PasswordChange);
        Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);
        Assert.Equal("csrf_rejected", await CodeAsync(crossOrigin));
        Assert.Equal(0, app.Control.PasswordChangeCalls);
    }

    [Fact]
    public async Task PasswordChangeUsesTheAuthenticatedAccountAndClearsTheCookieOnlyOnSuccess()
    {
        using var app = await Harness.StartAsync();
        app.Authenticate();
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        var insecure = await app.Client.PostAsJsonAsync($"http://localhost{PasswordChangePath}", PasswordChange);
        Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
        Assert.Equal("https_required", await CodeAsync(insecure));
        Assert.Equal(0, app.Control.PasswordChangeCalls);

        var incorrect = await app.Client.PostAsJsonAsync(PasswordChangePath, PasswordChange with { CurrentPassword = "synthetic-incorrect" });
        Assert.Equal(HttpStatusCode.BadRequest, incorrect.StatusCode);
        Assert.Equal("current_password_invalid", await CodeAsync(incorrect));
        Assert.False(incorrect.Headers.Contains("Set-Cookie"));

        var changed = await app.Client.PostAsJsonAsync(PasswordChangePath,
            new { PasswordChange.CurrentPassword, PasswordChange.NewPassword, userId = "999" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(9007199254740993L, app.Control.PasswordChangeUserId);
        var cookie = Assert.Single(changed.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-syslog=;", cookie);
        Assert.Contains("expires=", cookie);
        Assert.Contains("path=/", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        var body = await changed.Content.ReadAsStringAsync();
        Assert.DoesNotContain(PasswordChange.NewPassword!, body);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.GetAsync("/api/admin/v1/session")).StatusCode);
    }

    [Fact]
    public async Task PasswordChangeLimitsRepeatedAttempts()
    {
        using var app = await Harness.StartAsync();
        app.Authenticate();
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        var incorrect = PasswordChange with { CurrentPassword = "synthetic-incorrect" };
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync(PasswordChangePath, incorrect)).StatusCode);
        var rejected = await app.Client.PostAsJsonAsync(PasswordChangePath, incorrect);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("rate_limited", await CodeAsync(rejected));
        Assert.Equal(5, app.Control.PasswordChangeCalls);
    }

    [Fact]
    public async Task SearchUsesServerScopeAndStructuredContract()
    {
        using var app = await Harness.StartAsync();
        app.Authenticate();
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        var response = await app.Client.PostAsJsonAsync("/api/admin/v1/logs/search", new
        {
            from = "2026-11-09T00:00:00Z", to = "2026-11-10T00:00:00Z", keywords = "LINK DOWN",
            allowedSourceIds = new[] { "forged-source" }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["42"], app.Logs.LastScope);
        Assert.Equal("LINK DOWN", app.Logs.LastQuery!.Keywords);
        Assert.Equal("ok", (await CodeAsync(response)).ToLowerInvariant());
    }

    [Fact]
    public async Task DisabledSourceCanStillBeQueriedByAdmin()
    {
        using var app = await Harness.StartAsync();
        app.Authenticate();
        await app.Client.GetAsync("/api/admin/v1/logs/9?receivedAt=2026-11-10T00:00:00Z");
        Assert.Equal(["42"], app.Logs.LastScope);
    }

    [Fact]
    public async Task StoreFailureIsSanitizedAndNotSuccess()
    {
        using var app = await Harness.StartAsync();
        app.Authenticate();
        app.Control.ThrowOnRead = true;
        var response = await app.Client.GetAsync("/api/admin/v1/sources");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("service_unavailable", body);
        Assert.DoesNotContain("synthetic-secret", body);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("storage_timeout", 504)]
    [InlineData("storage_connection_failed", 503)]
    [InlineData("storage_access_denied", 503)]
    [InlineData("storage_schema_missing", 503)]
    [InlineData("unsupported_search_token", 400)]
    [InlineData("source_forbidden", 403)]
    public async Task StorageErrorsUseMeaningfulHttpStatus(string code, int expected)
    {
        using var app = await Harness.StartAsync();
        app.Authenticate();
        app.Logs.ErrorCode = code;
        var response = await app.Client.GetAsync("/api/admin/v1/logs/9?receivedAt=2026-11-10T00:00:00Z");
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
    }

    [Fact]
    public async Task MalformedModelUsesApiResultWithoutInternalDetails()
    {
        using var app = await Harness.StartAsync();
        app.Authenticate();
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        var response = await app.Client.PostAsJsonAsync("/api/admin/v1/logs/search", new { from = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await CodeAsync(response));
        Assert.Equal(0, app.Logs.Calls);
    }

    [Fact]
    public async Task LoginHasGlobalRateLimit()
    {
        using var app = await Harness.StartAsync();
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await app.Client.PostAsJsonAsync("/api/admin/v1/session/login", new { username = "operator", password = "synthetic-password" })).StatusCode);
        var rejected = await app.Client.PostAsJsonAsync("/api/admin/v1/session/login", new { username = "operator", password = "synthetic-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("rate_limited", await CodeAsync(rejected));
        Assert.Equal(10, app.Control.LoginCalls);
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(response.IsSuccessStatusCode, json.RootElement.GetProperty("success").GetBoolean());
        return json.RootElement.GetProperty("code").GetString()!;
    }

    private sealed class Harness(IHost host, HttpClient client, FakeControl control, FakeLogs logs) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public FakeControl Control { get; } = control;
        public FakeLogs Logs { get; } = logs;
        public void Authenticate() => Client.DefaultRequestHeaders.Add("Cookie", SessionAuthenticationHandler.CookieName + "=" + FakeControl.Token);
        public static async Task<Harness> StartAsync(SystemCertificateStore? certificates = null)
        {
            var control = new FakeControl();
            var logs = new FakeLogs();
            var host = await new HostBuilder().ConfigureLogging(x => x.ClearProviders()).ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSyslog(new ConfigurationBuilder().Build());
                    services.AddSingleton<IControlStore>(control);
                    services.AddSingleton<ILogStore>(logs);
                    if (certificates is not null) services.AddSingleton(certificates);
                }).Configure(app =>
                {
                    app.UseMiddleware<ApiBoundaryMiddleware>();
                    app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).StartAsync();
            var client = host.GetTestClient(); client.BaseAddress = new Uri("https://localhost");
            return new(host, client, control, logs);
        }
        public void Dispose() { Client.Dispose(); host.Dispose(); }
    }

    private sealed class FakeControl : IControlStore
    {
        public const string Token = "synthetic-session-for-http-contract-testing";
        private bool valid = true;
        public int LoginCalls;
        public int PasswordChangeCalls;
        public long PasswordChangeUserId;
        public bool ThrowOnRead;
        public bool AllowCertificateAudit;
        public CertificateSummary? AuditedCertificate;
        private static readonly ControlUser User = new(9007199254740993, "operator", "admin");
        public Task InitializeAsync(CancellationToken ct) => throw new InvalidOperationException("startup must not initialize");
        public Task<ApiResult<ControlUser>> BootstrapAdminAsync(string u, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsSetupRequiredAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<ApiResult<ControlUser>> SetupAdminAsync(string u, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiResult<ManagedUserPage>> ListUsersAsync(UserQuery q, long a, string t, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiResult<ManagedUser>> CreateUserAsync(CreateUserRequest i, long a, string t, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiResult<ManagedUser>> UpdateUserAsync(string id, UpdateUserRequest i, long a, string t, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiResult<bool>> ResetUserPasswordAsync(string id, ResetUserPasswordRequest i, long a, string t, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiResult<SessionTicket>> LoginAsync(string u, string p, CancellationToken ct) { LoginCalls++; return Task.FromResult(ApiResult<SessionTicket>.Ok(new(Token, DateTimeOffset.UtcNow.AddHours(1), User))); }
        public Task<ControlUser?> ValidateSessionAsync(string token, CancellationToken ct) => Task.FromResult(token == Token && valid ? User : null);
        public Task<ApiResult<bool>> AuthorizeHttpsCertificateUploadAsync(long actorId, string token, CertificateSummary certificate, CancellationToken ct)
        {
            if (actorId != User.Id || token != Token || !valid)
                return Task.FromResult(ApiResult<bool>.Fail("authentication_required", ""));
            if (!AllowCertificateAudit) return Task.FromResult(ApiResult<bool>.Fail("service_unavailable", "审计不可用。"));
            AuditedCertificate = certificate;
            return Task.FromResult(ApiResult<bool>.Ok(true));
        }
        public Task<ApiResult<bool>> ChangePasswordAsync(long id, string token, string? currentPassword, string? newPassword, CancellationToken ct)
        {
            PasswordChangeCalls++;
            PasswordChangeUserId = id;
            if (token != Token || id != User.Id) return Task.FromResult(ApiResult<bool>.Fail("authentication_required", ""));
            if (currentPassword != PasswordChange.CurrentPassword) return Task.FromResult(ApiResult<bool>.Fail("current_password_invalid", ""));
            valid = false;
            return Task.FromResult(ApiResult<bool>.Ok(true));
        }
        public Task<ApiResult<bool>> LogoutAsync(string t, CancellationToken ct) { valid = false; return Task.FromResult(ApiResult<bool>.Ok(true)); }
        public Task<IReadOnlyList<LogSource>> ListAsync(CancellationToken ct) => ThrowOnRead ? throw new Exception("path/synthetic-secret") : Task.FromResult<IReadOnlyList<LogSource>>([new("42", "switch-example", "192.0.2.10", "Asia/Shanghai", 1095, false)]);
        public Task<long> NextAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiResult<LogSource>> CreateSourceAsync(SourceInput i, long a, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiResult<LogSource>> UpdateSourceAsync(string id, SourceInput i, long a, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeLogs : ILogStore
    {
        public int Calls;
        public string[] LastScope = [];
        public LogSearch? LastQuery;
        public string? ErrorCode;
        public Task WriteBatchAsync(IReadOnlyList<LogEvent> e, CancellationToken ct) => throw new InvalidOperationException("disabled ingestion");
        public Task<LogSearchPage> SearchAsync(LogSearch q, IReadOnlyCollection<string> s, CancellationToken ct)
        { Calls++; LastScope = s.ToArray(); LastQuery = q; return Task.FromResult(new LogSearchPage([], null, false)); }
        public Task<LogEvent?> GetAsync(long id, DateTimeOffset at, IReadOnlyCollection<string> s, CancellationToken ct)
        { Calls++; LastScope = s.ToArray(); if (ErrorCode is not null) throw new ClickHouseStoreException(ErrorCode, "模拟存储错误"); return Task.FromResult<LogEvent?>(null); }
    }
}
