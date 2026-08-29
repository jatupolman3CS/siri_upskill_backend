using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Siri.Api.Authorization;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.ListSessions;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Features.Refresh;
using Siri.Modules.Identity.Features.RevokeAllSessions;
using Siri.Modules.Identity.Features.RevokeOtherSessions;
using Siri.Modules.Identity.Features.RevokeSession;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Real HTTP-level proof of P0-18's device-management API (ListSessions, RevokeSession,
/// RevokeOtherSessions, RevokeAllSessions) — through the actual ASP.NET Core authentication +
/// authorization + routing pipeline (<see cref="Microsoft.AspNetCore.TestHost"/>), the same
/// self-contained-<see cref="WebApplication"/>-with-explicit-configuration approach
/// <c>AuthorizationPolicyHttpTests</c> already established (see that class's own doc comment for why: no
/// path to the real Contabo database can ever open from a test, full stop). Requires Docker locally; see
/// <see cref="ContainersFixture"/>'s own doc comment — if Docker is not running, container startup fails
/// before any test body here runs, which is an environment issue, not a defect in these tests.
/// <para>
/// Sessions are created by resolving <see cref="LoginHandler"/> straight from the host's own DI
/// container (same pattern <c>LoginAndRefreshTests</c>/<c>ConcurrentSessionLimitTests</c> already use)
/// rather than posting to <c>/api/identity/login</c> over HTTP — this test file's actual subject is the
/// three new endpoints, and going through the handler directly gets a real access token/raw refresh
/// token pair without needing to parse the login endpoint's httpOnly Set-Cookie header. The three
/// endpoints under test <em>are</em> exercised over real HTTP, with that access token set as a Bearer
/// <see cref="AuthenticationHeaderValue"/> — this is what proves routing/auth/ownership actually work
/// end to end, not just the handlers in isolation.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class DeviceManagementTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "device-management-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public DeviceManagementTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
            ["Redis:ConnectionString"] = _containers.RedisConnectionString,
            ["Identity:EmailConfirmation:ConfirmEmailUrl"] = "https://example.test/confirm-email",
            ["Identity:PasswordReset:ResetPasswordUrl"] = "https://example.test/reset-password",
            // Generous on purpose — these tests deliberately log in several devices per user, and SE-03
            // eviction kicking in as an unrelated side effect would make every session-count assertion
            // below meaningless.
            ["Identity:Security:MaxConcurrentSessions"] = "10",
            ["Identity:Jwt:Issuer"] = TestIssuer,
            ["Identity:Jwt:Audience"] = TestAudience,
            ["Identity:Jwt:SigningKey"] = TestSigningKey,
            ["Identity:Jwt:AccessTokenLifetimeMinutes"] = "15",
            ["Email:Provider"] = "Log", // never a real SMTP send
        });

        // Mirrors Program.cs's own AddAuthentication/AddJwtBearer wiring — same shape
        // AuthorizationPolicyHttpTests already uses successfully.
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                    ValidateLifetime = true,
                };
            });

        builder.Services.AddSiriAuthorizationPolicies();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddNotificationModule(builder.Configuration);

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapIdentityEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();

        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync(); // no schema change in this task — still fine to run every migration
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static async Task<USER> CreateActiveUserAsync(IServiceProvider services, AppDbContext dbContext, string email, string password)
    {
        var passwordHasher = services.GetRequiredService<IUserPasswordHasher>();
        var clock = services.GetRequiredService<IClock>();

        var normalizedEmail = email.ToUpperInvariant();
        var throwaway = USER.Register(email, normalizedEmail, "placeholder", "Test USER");
        var hash = passwordHasher.HashPassword(throwaway, password);
        var user = USER.Register(email, normalizedEmail, hash, "Test USER");
        user.ConfirmEmail(clock);

        dbContext.Users().Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private readonly record struct LoggedInDevice(Guid SessionId, string AccessToken, string RawRefreshToken);

    /// <summary>Logs a device in via the real <see cref="LoginHandler"/> (not HTTP — see class doc
    /// comment) and pulls the session id straight out of the minted access token's own "sid" claim —
    /// exercising the exact same round-trip (<see cref="AccessTokenGenerator"/> writes it,
    /// <c>CurrentSessionClaim.Read</c>/these tests read it back) the endpoints under test rely on.</summary>
    private static async Task<LoggedInDevice> LoginDeviceAsync(IServiceProvider services, string email, string deviceId, string deviceName)
    {
        var loginHandler = services.GetRequiredService<LoginHandler>();
        var result = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, deviceId, deviceName), $"UA-{deviceId}", "203.0.113.50", CancellationToken.None);

        Assert.True(result.IsSuccess);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
        var sessionId = Guid.Parse(jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sid).Value);

        return new LoggedInDevice(sessionId, result.Value.AccessToken, result.Value.RawRefreshToken);
    }

    private static HttpRequestMessage AuthenticatedRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    // ---- ListSessions -----------------------------------------------------------------------------

    [Fact]
    public async Task ListSessions_ReturnsOnlyCallersOwnSessionsWithCorrectCurrentSessionFlag()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var emailA = $"list-a-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, emailA, KnownPassword);
        var deviceOne = await LoginDeviceAsync(scope.ServiceProvider, emailA, "device-1", "Device One");
        var deviceTwo = await LoginDeviceAsync(scope.ServiceProvider, emailA, "device-2", "Device Two");

        var emailB = $"list-b-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, emailB, KnownPassword);
        await LoginDeviceAsync(scope.ServiceProvider, emailB, "device-1", "Someone Else's Device");

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, "/api/identity/sessions", deviceOne.AccessToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ListSessionsResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(2, body!.Sessions.Count); // only user A's two devices — never user B's

        var own1 = body.Sessions.Single(s => s.SessionId == deviceOne.SessionId);
        var own2 = body.Sessions.Single(s => s.SessionId == deviceTwo.SessionId);
        Assert.True(own1.IsCurrentSession);
        Assert.False(own2.IsCurrentSession);
        Assert.Equal("Device One", own1.DeviceName);
        Assert.Equal("Device Two", own2.DeviceName);
        Assert.True(own1.IsActive);
        Assert.True(own2.IsActive);
    }

    [Theory]
    [InlineData("GET", "/api/identity/sessions")]
    [InlineData("DELETE", "/api/identity/sessions/00000000-0000-7000-8000-000000000000")]
    [InlineData("POST", "/api/identity/sessions/revoke-others")]
    [InlineData("POST", "/api/identity/sessions/revoke-all")]
    public async Task DeviceManagementEndpoint_NoAuthorizationHeader_Returns401(string method, string path)
    {
        // Proves the group's default-deny (IdentityModule.MapIdentityEndpoints) genuinely covers these
        // four new routes — not merely assumed from P0-22's retrofit of the OTHER six endpoints (task
        // instruction: "verify this concretely for these specific new routes").
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- RevokeSession ------------------------------------------------------------------------------

    /// <summary>The single most important test in this task (task instruction) — proves the ownership
    /// check in <see cref="RevokeSessionHandler"/> actually stops a caller from touching another
    /// account's session, AND that the response cannot be told apart from a genuinely nonexistent id.</summary>
    [Fact]
    public async Task RevokeSession_AnotherUsersSessionId_ReturnsSameNotFoundResponseAsGenuinelyNonexistentIdAndLeavesItUntouched()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var emailA = $"attacker-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, emailA, KnownPassword);
        var attackerDevice = await LoginDeviceAsync(scope.ServiceProvider, emailA, "device-1", "Attacker Device");

        var emailB = $"victim-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, emailB, KnownPassword);
        var victimDevice = await LoginDeviceAsync(scope.ServiceProvider, emailB, "device-1", "Victim Device");

        using var againstOtherUsersSession = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/identity/sessions/{victimDevice.SessionId}", attackerDevice.AccessToken));
        using var againstNonexistentId = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/identity/sessions/{Guid.NewGuid()}", attackerDevice.AccessToken));

        Assert.Equal(HttpStatusCode.NotFound, againstOtherUsersSession.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, againstNonexistentId.StatusCode);

        // Response bodies must be indistinguishable (task instruction: "do not distinguish 'doesn't
        // exist' from 'exists but isn't yours'"). traceId is unique per request by design (see
        // DomainErrorHttpResults) — strip it before comparing so the assertion is about the response
        // *shape/message*, not incidental per-request noise.
        var bodyOther = StripTraceId(await againstOtherUsersSession.Content.ReadAsStringAsync());
        var bodyNonexistent = StripTraceId(await againstNonexistentId.Content.ReadAsStringAsync());
        Assert.Equal(bodyNonexistent, bodyOther);

        // Not just "the response looked the same" — the ownership check must have actually prevented
        // any write to the victim's row.
        var victimSessionRow = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == victimDevice.SessionId);
        Assert.True(victimSessionRow.IsActive);
        Assert.Null(victimSessionRow.RevokedAtUtc);
    }

    private static string StripTraceId(string problemDetailsJson) =>
        Regex.Replace(problemDetailsJson, "\"traceId\"\\s*:\\s*\"[^\"]*\"", "\"traceId\":\"STRIPPED\"");

    [Fact]
    public async Task RevokeSession_OwnOtherSession_RevokesSessionAndRefreshTokenAndWritesAudit()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var refreshHandler = scope.ServiceProvider.GetRequiredService<RefreshHandler>();

        var email = $"revoke-one-{Guid.NewGuid():N}@example.test";
        var user = await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);
        var deviceOne = await LoginDeviceAsync(scope.ServiceProvider, email, "device-1", "Device One");
        var deviceTwo = await LoginDeviceAsync(scope.ServiceProvider, email, "device-2", "Device Two");

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/identity/sessions/{deviceTwo.SessionId}", deviceOne.AccessToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RevokeSessionResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(body!.WasCurrentSession);

        var revokedSessionRow = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == deviceTwo.SessionId);
        Assert.False(revokedSessionRow.IsActive);
        Assert.Equal("revoked_by_user", revokedSessionRow.RevokeReason);

        var audit = await dbContext.SecurityAudits().AsNoTracking()
            .SingleAsync(a => a.UserId == user.Id && a.EventType == "session.revoked_by_user");
        Assert.Contains(deviceTwo.SessionId.ToString(), audit.Detail!, StringComparison.OrdinalIgnoreCase);

        // Device one (the caller's own current session) must be completely untouched.
        var deviceOneRow = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == deviceOne.SessionId);
        Assert.True(deviceOneRow.IsActive);

        // The revoked session's refresh token must no longer work — same style of proof
        // ConcurrentSessionLimitTests/LoginAndRefreshTests already use.
        var refreshAttempt = await refreshHandler.HandleAsync(
            new RefreshCommand(deviceTwo.RawRefreshToken, "UA", "203.0.113.50"), CancellationToken.None);
        Assert.True(refreshAttempt.IsFailure);
    }

    [Fact]
    public async Task RevokeSession_OwnCurrentSession_SucceedsAndReportsWasCurrentSessionTrue()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"revoke-current-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);
        var device = await LoginDeviceAsync(scope.ServiceProvider, email, "device-1", "Only Device");

        // Proves the "a caller MAY revoke their own current session" design decision
        // (RevokeSessionHandler's doc comment) actually works — not blocked.
        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/identity/sessions/{device.SessionId}", device.AccessToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RevokeSessionResponse>(JsonOptions);
        Assert.True(body!.WasCurrentSession);

        var sessionRow = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == device.SessionId);
        Assert.False(sessionRow.IsActive);
    }

    [Fact]
    public async Task RevokeSession_AlreadyRevokedSession_ReturnsSameNotFoundResponse()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"double-revoke-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);
        var deviceOne = await LoginDeviceAsync(scope.ServiceProvider, email, "device-1", "Device One");
        var deviceTwo = await LoginDeviceAsync(scope.ServiceProvider, email, "device-2", "Device Two");

        using var firstAttempt = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/identity/sessions/{deviceTwo.SessionId}", deviceOne.AccessToken));
        Assert.Equal(HttpStatusCode.OK, firstAttempt.StatusCode);

        using var secondAttempt = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/identity/sessions/{deviceTwo.SessionId}", deviceOne.AccessToken));
        Assert.Equal(HttpStatusCode.NotFound, secondAttempt.StatusCode);
    }

    // ---- RevokeOtherSessions ---------------------------------------------------------------------

    [Fact]
    public async Task RevokeOtherSessions_RevokesEveryOtherSessionButKeepsCurrentActive()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var refreshHandler = scope.ServiceProvider.GetRequiredService<RefreshHandler>();

        var email = $"revoke-others-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);
        var deviceOne = await LoginDeviceAsync(scope.ServiceProvider, email, "device-1", "Device One");
        var deviceTwo = await LoginDeviceAsync(scope.ServiceProvider, email, "device-2", "Device Two");
        var deviceThree = await LoginDeviceAsync(scope.ServiceProvider, email, "device-3", "Device Three");

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Post, "/api/identity/sessions/revoke-others", deviceOne.AccessToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RevokeOtherSessionsResponse>(JsonOptions);
        Assert.Equal(2, body!.RevokedCount);

        var deviceOneRow = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == deviceOne.SessionId);
        var deviceTwoRow = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == deviceTwo.SessionId);
        var deviceThreeRow = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == deviceThree.SessionId);

        Assert.True(deviceOneRow.IsActive); // the caller's own current session — must survive
        Assert.False(deviceTwoRow.IsActive);
        Assert.False(deviceThreeRow.IsActive);
        Assert.Equal("revoked_by_user_bulk_others", deviceTwoRow.RevokeReason);

        var refreshAttemptTwo = await refreshHandler.HandleAsync(
            new RefreshCommand(deviceTwo.RawRefreshToken, "UA", "203.0.113.50"), CancellationToken.None);
        Assert.True(refreshAttemptTwo.IsFailure);

        // device-1's own refresh token must still work — this endpoint must never touch the caller's
        // own current session.
        var refreshAttemptOne = await refreshHandler.HandleAsync(
            new RefreshCommand(deviceOne.RawRefreshToken, "UA", "203.0.113.50"), CancellationToken.None);
        Assert.True(refreshAttemptOne.IsSuccess);
    }

    [Fact]
    public async Task RevokeOtherSessions_NoOtherActiveSessions_SucceedsAsNoOpAndKeepsCurrentSessionActive()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"revoke-others-noop-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);
        var device = await LoginDeviceAsync(scope.ServiceProvider, email, "device-1", "Only Device");

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Post, "/api/identity/sessions/revoke-others", device.AccessToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RevokeOtherSessionsResponse>(JsonOptions);
        Assert.Equal(0, body!.RevokedCount);

        var sessionRow = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == device.SessionId);
        Assert.True(sessionRow.IsActive);
    }

    // ---- RevokeAllSessions ----------------------------------------------------------------------

    [Fact]
    public async Task RevokeAllSessions_RevokesEveryoneIncludingCurrentSession()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var refreshHandler = scope.ServiceProvider.GetRequiredService<RefreshHandler>();

        var email = $"revoke-all-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);
        var deviceOne = await LoginDeviceAsync(scope.ServiceProvider, email, "device-1", "Device One");
        var deviceTwo = await LoginDeviceAsync(scope.ServiceProvider, email, "device-2", "Device Two");

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Post, "/api/identity/sessions/revoke-all", deviceOne.AccessToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RevokeAllSessionsResponse>(JsonOptions);
        Assert.Equal(2, body!.RevokedCount);

        var deviceOneRow = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == deviceOne.SessionId);
        var deviceTwoRow = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == deviceTwo.SessionId);
        Assert.False(deviceOneRow.IsActive); // unlike RevokeOtherSessions, the caller's own session IS revoked too
        Assert.False(deviceTwoRow.IsActive);
        Assert.Equal("revoked_by_user_bulk_all", deviceOneRow.RevokeReason);

        var refreshAttemptOne = await refreshHandler.HandleAsync(
            new RefreshCommand(deviceOne.RawRefreshToken, "UA", "203.0.113.50"), CancellationToken.None);
        Assert.True(refreshAttemptOne.IsFailure);

        var refreshAttemptTwo = await refreshHandler.HandleAsync(
            new RefreshCommand(deviceTwo.RawRefreshToken, "UA", "203.0.113.50"), CancellationToken.None);
        Assert.True(refreshAttemptTwo.IsFailure);
    }
}
