using Siri.Modules.Live.Application;
using Siri.Modules.Live.Infrastructure;
using StackExchange.Redis;

namespace Siri.UnitTests.Live;

/// <summary>The OAuth state store must fail CLOSED (no Redis => no state => the OAuth flow cannot proceed) and must never log the state or verifier.
/// The happy path (single-use consume against a real Redis) is covered by the Testcontainers integration tests.</summary>
public class RedisGoogleOAuthStateStoreTests
{
    private static ConnectionMultiplexer Unreachable() =>
        ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200,asyncTimeout=200");

    [Fact]
    public async Task Save_RedisUnreachable_ReportsFailure_NeverPretendsTheStateWasStored()
    {
        using var redis = Unreachable();
        var logger = new ListLogger<RedisGoogleOAuthStateStore>();
        var store = new RedisGoogleOAuthStateStore(redis, logger);

        var saved = await store.TrySaveAsync("state-value-xyz", new GoogleOAuthState(Guid.NewGuid(), "verifier-secret-abc", "/instructor/live-settings"), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.False(saved);
        Assert.DoesNotContain("state-value-xyz", logger.All);
        Assert.DoesNotContain("verifier-secret-abc", logger.All);
    }

    [Fact]
    public async Task Consume_RedisUnreachable_ReturnsNull_SoNoCallbackCanSucceed()
    {
        using var redis = Unreachable();
        var store = new RedisGoogleOAuthStateStore(redis, new ListLogger<RedisGoogleOAuthStateStore>());

        var consumed = await store.TryConsumeAsync("state-value-xyz", CancellationToken.None);

        Assert.Null(consumed);
    }

    [Fact]
    public async Task Consume_EmptyState_ReturnsNullWithoutTouchingRedis()
    {
        using var redis = Unreachable();
        var store = new RedisGoogleOAuthStateStore(redis, new ListLogger<RedisGoogleOAuthStateStore>());

        Assert.Null(await store.TryConsumeAsync(string.Empty, CancellationToken.None));
    }
}
