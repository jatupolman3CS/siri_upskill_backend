using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// <c>GET /api/payout/policy</c> end to end over real HTTP against the production composition root (<see cref="SiriApiFactory"/>): JWT bearer middleware, the
/// controller, the per-user rate limit, and <c>PayoutPolicyService</c>'s real reads (the caller's instructor profile and its stored revenue share) — the parts the
/// unit suite cannot reach. Requires PostgreSQL + Redis like every test in this collection (Docker, or the local external-services mode of
/// <see cref="ExternalTestServices"/>).
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class PayoutPolicyEndpointTests : IAsyncLifetime
{
    private const string PolicyPath = "/api/payout/policy";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public PayoutPolicyEndpointTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);
        await MigrateAsync(_factory);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Migrations are applied here, not by the app (database.md: never Database.Migrate() in Program.cs).
    private static async Task MigrateAsync(SiriApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    private async Task<(TestUserBuilder Builder, USER User)> CreateUserAsync(string? role = null)
    {
        var builder = new TestUserBuilder();
        if (role is not null)
        {
            builder.WithRole(role);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);

        return (builder, user);
    }

    /// <summary>Gives <paramref name="userId"/> an approved instructor profile; <paramref name="sharePercent"/> overrides the 70.00 default the factory method stamps.</summary>
    private async Task<Guid> CreateInstructorProfileAsync(Guid userId, decimal? sharePercent = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var profile = INSTRUCTOR_PROFILE.Apply(userId, "Policy Instructor", "Headline", "Bio");
        profile.Approve(clock);
        db.InstructorProfiles().Add(profile);
        if (sharePercent is { } share)
        {
            db.Entry(profile).Property(p => p.RevenueSharePercent).CurrentValue = share;
        }

        await db.SaveChangesAsync();
        return profile.Id;
    }

    private async Task<string> LoginAsync(TestUserBuilder builder, HttpClient? client = null)
    {
        using var response = await (client ?? _client).PostAsJsonAsync("/api/identity/login", new
        {
            email = builder.Email,
            password = builder.Password,
            deviceId = $"payout-policy-{Guid.NewGuid():N}",
            deviceName = "Payout Policy Test Device",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);

        return body.AccessToken;
    }

    private static HttpRequestMessage PolicyRequest(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, PolicyPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return request;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task GetPolicy_WithoutBearerToken_Returns401()
    {
        using var response = await _client.GetAsync(PolicyPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPolicy_SignedInLearner_ReturnsTheDefaultPolicyWithTheExactJsonContractAndNoStore()
    {
        var (builder, _) = await CreateUserAsync(ROLE.LearnerName);
        var token = await LoginAsync(builder);

        using var response = await _client.SendAsync(PolicyRequest(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        // Raw JSON, not a deserialized record: this is the contract the frontend codes against.
        var root = await ReadJsonAsync(response);
        Assert.Equal(
            ["holdDays", "minimumPayoutAmount", "platformSharePercent", "revenueSharePercent", "withholdingTaxPercent"],
            root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(70m, root.GetProperty("revenueSharePercent").GetDecimal());
        Assert.Equal(30m, root.GetProperty("platformSharePercent").GetDecimal());
        Assert.Equal(3m, root.GetProperty("withholdingTaxPercent").GetDecimal());
        Assert.Equal(500m, root.GetProperty("minimumPayoutAmount").GetDecimal());
        Assert.Equal(14, root.GetProperty("holdDays").GetInt32());
    }

    [Fact]
    public async Task GetPolicy_InstructorWithTheDefaultRate_Gets70And30()
    {
        var (builder, user) = await CreateUserAsync(ROLE.InstructorName);
        await CreateInstructorProfileAsync(user.Id);
        var token = await LoginAsync(builder);

        using var response = await _client.SendAsync(PolicyRequest(token));
        var root = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(70m, root.GetProperty("revenueSharePercent").GetDecimal());
        Assert.Equal(30m, root.GetProperty("platformSharePercent").GetDecimal());
    }

    [Fact]
    public async Task GetPolicy_InstructorWithACustomRate_GetsTheirOwnRateAndNeverAnotherInstructors()
    {
        var (aliceBuilder, alice) = await CreateUserAsync(ROLE.InstructorName);
        var (bobBuilder, bob) = await CreateUserAsync(ROLE.InstructorName);
        await CreateInstructorProfileAsync(alice.Id, sharePercent: 80.00m);
        await CreateInstructorProfileAsync(bob.Id, sharePercent: 65.50m);
        var aliceToken = await LoginAsync(aliceBuilder);
        var bobToken = await LoginAsync(bobBuilder);

        using var aliceResponse = await _client.SendAsync(PolicyRequest(aliceToken));
        using var bobResponse = await _client.SendAsync(PolicyRequest(bobToken));
        var aliceJson = await ReadJsonAsync(aliceResponse);
        var bobJson = await ReadJsonAsync(bobResponse);

        Assert.Equal(80.00m, aliceJson.GetProperty("revenueSharePercent").GetDecimal());
        Assert.Equal(20.00m, aliceJson.GetProperty("platformSharePercent").GetDecimal());
        Assert.Equal(65.50m, bobJson.GetProperty("revenueSharePercent").GetDecimal());
        Assert.Equal(34.50m, bobJson.GetProperty("platformSharePercent").GetDecimal());
    }

    [Fact]
    public async Task GetPolicy_ThePayoutOptionsComeFromConfiguration_NotFromTheCode()
    {
        await using var configured = new SiriApiFactory(_containers, new Dictionary<string, string?>
        {
            ["Payout:WithholdingTaxPercent"] = "5.00",
            ["Payout:MinimumPayoutAmount"] = "1200.00",
            ["Payout:HoldDays"] = "7",
        });
        await MigrateAsync(configured);
        using var client = configured.CreateClient();

        var builder = new TestUserBuilder();
        await using (var scope = configured.Services.CreateAsyncScope())
        {
            await builder.WithRole(ROLE.LearnerName).BuildAsync(scope.ServiceProvider);
        }

        var token = await LoginAsync(builder, client);
        using var request = PolicyRequest(token);
        using var response = await client.SendAsync(request);
        var root = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5m, root.GetProperty("withholdingTaxPercent").GetDecimal());
        Assert.Equal(1200m, root.GetProperty("minimumPayoutAmount").GetDecimal());
        Assert.Equal(7, root.GetProperty("holdDays").GetInt32());
        Assert.Equal(70m, root.GetProperty("revenueSharePercent").GetDecimal());
    }

    [Fact]
    public async Task GetPolicy_RateLimitIsPerUser_The61stRequestInAMinuteIsA429ProblemAndAnotherUserIsUnaffected()
    {
        var (burstBuilder, _) = await CreateUserAsync(ROLE.LearnerName);
        var (otherBuilder, _) = await CreateUserAsync(ROLE.LearnerName);
        var burstToken = await LoginAsync(burstBuilder);
        var otherToken = await LoginAsync(otherBuilder);

        for (var i = 1; i <= 60; i++)
        {
            using var request = PolicyRequest(burstToken);
            using var allowed = await _client.SendAsync(request);
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, $"request {i} should be allowed but was {(int)allowed.StatusCode}");
        }

        using var rejectedRequest = PolicyRequest(burstToken);
        using var rejected = await _client.SendAsync(rejectedRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", rejected.Headers.CacheControl?.ToString() ?? string.Empty);
        Assert.Equal("rate_limited", (await ReadJsonAsync(rejected)).GetProperty("errorCode").GetString());

        using var otherRequest = PolicyRequest(otherToken);
        using var other = await _client.SendAsync(otherRequest);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }
}
