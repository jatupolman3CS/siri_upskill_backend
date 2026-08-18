using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Identity.Features.Login;
using Siri.Persistence;

namespace Siri.IntegrationTests;

/// <summary>
/// Proves the P0-20 harness itself: <see cref="SiriApiFactory"/> boots the REAL <c>Siri.Api</c>
/// host (<c>Program.cs</c> — Serilog, JwtBearer, Hangfire, rate limiting, CORS, the
/// <c>/api/identity</c> group's default-deny, everything) against the Testcontainers instances, and
/// <see cref="TestUserBuilder"/> seeds users into it. The earlier integration test files each
/// exercise handlers through hand-built minimal hosts; this file is the one place that exercises
/// the production composition root end to end over real HTTP — anything <c>Program.cs</c> wires
/// wrong (middleware order, missing registration, options binding) surfaces here first.
/// <para>
/// The two "host boot" facts double as regression tests for <see cref="SiriApiFactory"/>'s safety
/// guarantees (environment + connection-string override) — see its doc comment for why those must
/// hold. Requires Docker like every test in this collection; without it,
/// <see cref="ContainersFixture"/> fails startup before any test body runs.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class ApiHostSmokeTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ApiHostSmokeTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        // Accessing .Services boots the host (this is where SiriApiFactory's fail-fast guard runs).
        // Migrations are applied here, not by the app — Program.cs must never call
        // Database.Migrate() (database.md: production migrates via bundle only).
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public void HostBoot_RunsUnderIntegrationTestEnvironment_NeverDevelopment()
    {
        // Development would load this machine's user-secrets (real Contabo connection string).
        var environment = _factory.Services.GetRequiredService<IHostEnvironment>();
        Assert.Equal(SiriApiFactory.EnvironmentName, environment.EnvironmentName);
    }

    [Fact]
    public void HostBoot_ConfigurationPointsAtTestcontainersInstances_NotUserSecrets()
    {
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();

        Assert.Equal(_containers.SqlConnectionString, configuration.GetConnectionString("Default"));
        Assert.Equal(_containers.RedisConnectionString, configuration["Redis:ConnectionString"]);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutBearerToken_Returns401ThroughRealPipeline()
    {
        // /api/identity/sessions has no .AllowAnonymous() — the group-level default-deny (P0-22)
        // must reject an anonymous request via the real JwtBearer middleware, not endpoint code.
        var response = await _client.GetAsync("/api/identity/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UserSeededByBuilder_SucceedsOverRealHttpAndSetsRefreshCookie()
    {
        var builder = new TestUserBuilder();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await builder.BuildAsync(scope.ServiceProvider);
        }

        var response = await _client.PostAsJsonAsync("/api/identity/login", new
        {
            email = builder.Email,
            password = builder.Password,
            deviceId = "smoke-device",
            deviceName = "Smoke Test Device",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.AccessToken);

        // Refresh token travels only as an httpOnly cookie, never in the JSON body (security.md).
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies));
        Assert.Contains(setCookies, cookie => cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }
}
