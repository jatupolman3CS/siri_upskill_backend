using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.GetMe;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;

namespace Siri.IntegrationTests;

/// <summary>
/// GET /api/identity/me end to end over real HTTP against the production composition root
/// (<see cref="SiriApiFactory"/>) — JWT bearer middleware, the controller, <see cref="GetMeHandler"/>'s
/// real EF query (the part the unit suite cannot reach), and the response headers. Requires Docker like
/// every test in this collection.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class MeEndpointTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public MeEndpointTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        // Migrations are applied here, not by the app (database.md: never Database.Migrate() in Program.cs).
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(TestUserBuilder Builder, USER User)> CreateUserAsync(Action<TestUserBuilder>? configure = null)
    {
        var builder = new TestUserBuilder();
        configure?.Invoke(builder);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);

        return (builder, user);
    }

    private async Task<string> LoginAsync(TestUserBuilder builder)
    {
        using var response = await _client.PostAsJsonAsync("/api/identity/login", new
        {
            email = builder.Email,
            password = builder.Password,
            deviceId = $"me-test-{Guid.NewGuid():N}",
            deviceName = "Me Test Device",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);

        return body.AccessToken;
    }

    private static HttpRequestMessage MeRequest(string accessToken, string path = "/api/identity/me")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return request;
    }

    [Fact]
    public async Task GetMe_WithoutBearerToken_Returns401()
    {
        using var response = await _client.GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_SignedInUser_ReturnsOwnProfileWithTheExactJsonContract()
    {
        var (builder, user) = await CreateUserAsync(b => b
            .WithDisplayName("Me Person")
            .WithRole(ROLE.LearnerName)
            .WithRole(ROLE.InstructorName));
        var token = await LoginAsync(builder);

        using var response = await _client.SendAsync(MeRequest(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Raw JSON, not a deserialized record: this is the contract the frontend codes against.
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(
            ["avatarUrl", "displayName", "email", "id", "roles"],
            root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(user.Id, root.GetProperty("id").GetGuid());
        Assert.Equal(builder.Email, root.GetProperty("email").GetString());
        Assert.Equal("Me Person", root.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("avatarUrl").ValueKind);
        Assert.Equal(
            [ROLE.InstructorName, ROLE.LearnerName],
            root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());
    }

    [Fact]
    public async Task GetMe_UserWithAvatar_ReturnsTheAvatarUrl()
    {
        var (builder, user) = await CreateUserAsync();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.Users().SingleAsync(u => u.Id == user.Id);
            Assert.True(tracked.SetAvatarIfMissing("https://lh3.googleusercontent.com/a/me-test=s96-c"));
            await db.SaveChangesAsync();
        }
        var token = await LoginAsync(builder);

        using var response = await _client.SendAsync(MeRequest(token));
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://lh3.googleusercontent.com/a/me-test=s96-c", body!.AvatarUrl);
    }

    [Fact]
    public async Task GetMe_Response_IsMarkedNoStoreSoPersonalDataIsNeverCached()
    {
        var (builder, _) = await CreateUserAsync();
        var token = await LoginAsync(builder);

        using var response = await _client.SendAsync(MeRequest(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task GetMe_ClientSuppliedUserIdInQueryOrRoute_IsIgnoredAndTheCallerStillGetsOnlyTheirOwnProfile()
    {
        var (callerBuilder, caller) = await CreateUserAsync(b => b.WithDisplayName("Caller"));
        var (_, other) = await CreateUserAsync(b => b.WithDisplayName("Somebody Else"));
        var token = await LoginAsync(callerBuilder);

        using var response = await _client.SendAsync(MeRequest(token, $"/api/identity/me?userId={other.Id}&id={other.Id}"));
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(caller.Id, body!.Id);
        Assert.Equal("Caller", body.DisplayName);
        Assert.NotEqual(other.Id, body.Id);
    }

    [Fact]
    public async Task GetMe_AccessTokenOfAnAnonymizedAccount_Returns404ProblemDetailsWithoutThePlaceholderIdentity()
    {
        var (builder, user) = await CreateUserAsync();
        var token = await LoginAsync(builder); // minted before the erasure, still cryptographically valid

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.Users().SingleAsync(u => u.Id == user.Id);
            tracked.Anonymize($"anonymized_{user.Id:N}@deleted.example", "Deleted USER", "unmatchable-hash");
            await db.SaveChangesAsync();
        }

        using var response = await _client.SendAsync(MeRequest(token));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("anonymized_", raw);
        Assert.DoesNotContain("Deleted USER", raw);
        Assert.True(response.Headers.CacheControl?.NoStore); // errors are never cacheable either
    }
}
