using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// GET /api/notifications/unread-count over real HTTP against the production composition root: authorization, that the count is the
/// caller's own (it can only come from the token), and that reading a notification moves the number straight away through the cache.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class UnreadNotificationCountEndpointTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public UnreadNotificationCountEndpointTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private sealed class SystemClockForTests : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private async Task<(Guid UserId, string Token)> SignedInUserAsync()
    {
        var builder = new TestUserBuilder();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var user = await builder.BuildAsync(scope.ServiceProvider);
            using var response = await _client.PostAsJsonAsync("/api/identity/login", new
            {
                email = builder.Email,
                password = builder.Password,
                deviceId = $"unread-test-{Guid.NewGuid():N}",
                deviceName = "Unread Test Device",
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
            return (user.Id, body!.AccessToken);
        }
    }

    private async Task<Guid> AddNotificationAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = USER_NOTIFICATION.Create(userId, "endpoint.test", "Title", "Body", null, new SystemClockForTests());
        db.UserNotifications().Add(notification);
        await db.SaveChangesAsync();
        return notification.Id;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<int> UnreadCountAsync(string token)
    {
        using var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/notifications/unread-count", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // The exact JSON contract the frontend codes against.
        Assert.Equal(["unreadCount"], json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        return json.RootElement.GetProperty("unreadCount").GetInt32();
    }

    [Fact]
    public async Task GetUnreadCount_WithoutBearerToken_Returns401()
    {
        using var response = await _client.GetAsync("/api/notifications/unread-count");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUnreadCount_NewUser_IsZero()
    {
        var (_, token) = await SignedInUserAsync();

        Assert.Equal(0, await UnreadCountAsync(token));
    }

    [Fact]
    public async Task GetUnreadCount_CountsOnlyTheCallersOwnUnreadNotifications()
    {
        var (userId, token) = await SignedInUserAsync();
        var (otherUserId, _) = await SignedInUserAsync();
        await AddNotificationAsync(userId);
        await AddNotificationAsync(userId);
        await AddNotificationAsync(otherUserId);

        Assert.Equal(2, await UnreadCountAsync(token));
    }

    [Fact]
    public async Task MarkRead_ThenGetUnreadCount_ReflectsItImmediately()
    {
        var (userId, token) = await SignedInUserAsync();
        var first = await AddNotificationAsync(userId);
        await AddNotificationAsync(userId);
        Assert.Equal(2, await UnreadCountAsync(token)); // now cached

        using var read = await _client.SendAsync(Request(HttpMethod.Post, $"/api/notifications/{first}/read", token));
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        Assert.Equal(1, await UnreadCountAsync(token));
    }
}
