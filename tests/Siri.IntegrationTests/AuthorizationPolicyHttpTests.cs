using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Siri.Api.Authorization;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Notification;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Real HTTP-level proof of the P0-22 default-deny retrofit and the AdminOnly/InstructorOnly policies,
/// through the actual ASP.NET Core authentication + authorization middleware pipeline (JwtBearer
/// validation, <c>RequireAuthorization</c>/<c>AllowAnonymous</c> route metadata) — over a real
/// in-process HTTP client (<see cref="Microsoft.AspNetCore.TestHost"/>), not the production
/// <c>Siri.Api</c> host (<c>Program.cs</c>) itself. That is deliberate, not an oversight:
/// <list type="bullet">
/// <item>Booting the real host via <c>WebApplicationFactory&lt;Program&gt;</c> would need
/// <c>Program.cs</c>'s own top-level configuration reads (the JWT signing key check, and
/// <c>Siri.Workers.AddWorkers</c>' <c>ConnectionStrings:Default</c> check for Hangfire's SQL storage)
/// to already be satisfied by this test's configuration overrides at the exact moment those lines of
/// <c>Program.cs</c> run — a timing guarantee this environment cannot verify by actually running the
/// test (no Docker here). If that assumption were ever wrong, the app could silently fall back to
/// whatever <c>ConnectionStrings:Default</c> happens to already be configured on the machine running
/// the test (this repo's dev user-secrets point at the real Contabo SQL Server — see CLAUDE.md) — a
/// risk this task's own constraints explicitly forbid ("Do not connect to the real remote database...
/// regardless"). Building a small, self-contained <see cref="WebApplication"/> here instead means
/// every configuration value is explicit, in this file, from <see cref="ContainersFixture"/> — there
/// is no path to a real database connection.</item>
/// <item>It also avoids pulling in Hangfire/all the other (still-empty) modules Program.cs wires up,
/// none of which this test needs.</item>
/// </list>
/// <para>
/// Uses <c>Microsoft.AspNetCore.TestHost</c> (<c>builder.WebHost.UseTestServer()</c> +
/// <c>app.GetTestClient()</c>) — the officially documented lightweight way to test a minimal API
/// in-process — instead of a real socket/Kestrel.
/// </para>
/// <para>
/// The <c>/__test/*</c> endpoints below exist ONLY inside this file's own <see cref="WebApplication"/>
/// instance; <c>IdentityModule.MapIdentityEndpoints</c> (production routing) is never touched by this
/// file. They exist purely to exercise <see cref="Siri.SharedKernel.AuthorizationPolicyNames.AdminOnly"/>/
/// <see cref="Siri.SharedKernel.AuthorizationPolicyNames.InstructorOnly"/> end to end over real HTTP,
/// independent of whichever module endpoints happen to use them in production routing at any given
/// time (P1-01's Catalog admin endpoints are the first real <c>AdminOnly</c> consumer, but this file
/// predates that and still exercises the policies directly rather than through Catalog) —
/// per this task's own suggested alternative to adding a throwaway endpoint to production routing.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class AuthorizationPolicyHttpTests : IAsyncLifetime
{
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "authorization-policy-http-tests-signing-key-0123456789";

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public AuthorizationPolicyHttpTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Every value this minimal host needs, explicit and local to this test — see this class's own
        // doc comment for why that matters. Same keys/shape LoginAndRefreshTests.cs already uses
        // successfully for the same three module registrations below.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
            ["Redis:ConnectionString"] = _containers.RedisConnectionString,
            ["Identity:EmailConfirmation:ConfirmEmailUrl"] = "https://example.test/confirm-email",
            ["Identity:PasswordReset:ResetPasswordUrl"] = "https://example.test/reset-password",
            ["Identity:Security:MaxConcurrentSessions"] = "2",
            ["Identity:Jwt:Issuer"] = TestIssuer,
            ["Identity:Jwt:Audience"] = TestAudience,
            ["Identity:Jwt:SigningKey"] = TestSigningKey,
            ["Identity:Jwt:AccessTokenLifetimeMinutes"] = "15",
            ["Email:Provider"] = "Log", // never a real SMTP send
        });

        // Mirrors Program.cs's own AddAuthentication/AddJwtBearer wiring, reading the same
        // Identity:Jwt:* configuration used above and to mint tokens in BuildJwt below, so a token
        // built here validates exactly the way a real Login-issued token would against the real host.
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

        // Test-only — see this class's own doc comment. Never mapped onto the production app.
        _app.MapGet("/__test/admin-only", () => Results.Ok()).RequireAuthorization(AuthorizationPolicyNames.AdminOnly);
        _app.MapGet("/__test/instructor-only", () => Results.Ok()).RequireAuthorization(AuthorizationPolicyNames.InstructorOnly);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static string BuildJwt(params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            TestIssuer, TestAudience, claims, notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private void AuthenticateAs(params string[] roles) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildJwt(roles));

    // ---- Group-level default-deny retrofit: the four genuinely public Identity endpoints ----------

    [Theory]
    [InlineData("/api/identity/register")]
    [InlineData("/api/identity/confirm-email")]
    [InlineData("/api/identity/login")]
    public async Task PublicIdentityEndpoint_NoAuthorizationHeader_IsNotBlockedByAuthorizationMiddleware(string path)
    {
        // An empty body is intentional — deliberately not exercising the real business logic
        // (Register/Login success is already covered by RegisterAndConfirmEmailTests.cs/
        // LoginAndRefreshTests.cs). This only asks: did the request even reach the endpoint (400 from
        // validation/model binding), or did the group's default-deny wrongly block it (401)? A 401
        // here would mean this specific endpoint's .AllowAnonymous() is missing/broken.
        using var response = await _client.PostAsync(path, new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RefreshEndpoint_NoAuthorizationHeaderAndNoCookie_IsNotBlockedByAuthorizationMiddleware()
    {
        // Refresh reads its token from a cookie, not a JSON body (see RefreshEndpoint's own doc
        // comment) — no cookie here is itself the "invalid refresh token" case the handler already
        // rejects with a generic 400, same reasoning as the Theory above: only 401/403 would indicate
        // the group-level retrofit broke this endpoint's AllowAnonymous().
        using var response = await _client.PostAsync("/api/identity/refresh", content: null);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- AdminOnly / InstructorOnly, exercised end to end via the /__test/* endpoints --------------

    [Fact]
    public async Task AdminOnlyEndpoint_NoAuthorizationHeader_Returns401()
    {
        using var response = await _client.GetAsync("/__test/admin-only");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(ROLE.LearnerName)]
    [InlineData(ROLE.InstructorName)]
    public async Task AdminOnlyEndpoint_TokenWithoutAdminRole_Returns403(string role)
    {
        AuthenticateAs(role);

        using var response = await _client.GetAsync("/__test/admin-only");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(ROLE.AdminName)]
    [InlineData(ROLE.SuperAdminName)]
    public async Task AdminOnlyEndpoint_TokenWithAdminOrSuperAdminRole_Returns200(string role)
    {
        AuthenticateAs(role);

        using var response = await _client.GetAsync("/__test/admin-only");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InstructorOnlyEndpoint_NoAuthorizationHeader_Returns401()
    {
        using var response = await _client.GetAsync("/__test/instructor-only");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InstructorOnlyEndpoint_TokenWithOnlyLearnerRole_Returns403()
    {
        AuthenticateAs(ROLE.LearnerName);

        using var response = await _client.GetAsync("/__test/instructor-only");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(ROLE.InstructorName)]
    [InlineData(ROLE.AdminName)]
    [InlineData(ROLE.SuperAdminName)]
    public async Task InstructorOnlyEndpoint_TokenWithInstructorOrAdminOrSuperAdminRole_Returns200(string role)
    {
        AuthenticateAs(role);

        using var response = await _client.GetAsync("/__test/instructor-only");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
