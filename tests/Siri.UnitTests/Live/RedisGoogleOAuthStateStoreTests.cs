using Siri.Modules.Live.Application;
using Siri.Modules.Live.Infrastructure;
using Siri.SharedKernel;
using StackExchange.Redis;

namespace Siri.UnitTests.Live;

/// <summary>With Redis down the OAuth state is kept in this process (so the connect button still works), and it must stay recorded, single-use,
/// short-lived and bounded, and must never be logged. The happy path against a real Redis is covered by the integration tests.</summary>
public class RedisGoogleOAuthStateStoreTests
{
    private static readonly GoogleOAuthState Payload = new(Guid.NewGuid(), "verifier-secret-abc", "/instructor/live-settings");

    private static ConnectionMultiplexer Unreachable() =>
        ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200,asyncTimeout=200");

    private sealed class MutableClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    private static readonly DateTime Start = new(2026, 10, 10, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Save_RedisUnreachable_KeepsTheStateInMemory_AndConsumeReturnsItOnce()
    {
        using var redis = Unreachable();
        var logger = new ListLogger<RedisGoogleOAuthStateStore>();
        var store = new RedisGoogleOAuthStateStore(redis, new MutableClock(Start), logger);

        var saved = await store.TrySaveAsync("state-value-xyz", Payload, TimeSpan.FromMinutes(10), CancellationToken.None);
        var first = await store.TryConsumeAsync("state-value-xyz", CancellationToken.None);
        var replay = await store.TryConsumeAsync("state-value-xyz", CancellationToken.None);

        Assert.True(saved);
        Assert.Equal(Payload, first);
        Assert.Null(replay); // single use: the second callback with the same state gets nothing
    }

    [Fact]
    public async Task Save_RedisUnreachable_NeverLogsTheStateOrTheVerifier_ButSaysItFellBack()
    {
        using var redis = Unreachable();
        var logger = new ListLogger<RedisGoogleOAuthStateStore>();
        var store = new RedisGoogleOAuthStateStore(redis, new MutableClock(Start), logger);

        await store.TrySaveAsync("state-value-xyz", Payload, TimeSpan.FromMinutes(10), CancellationToken.None);
        await store.TryConsumeAsync("state-value-xyz", CancellationToken.None);

        Assert.DoesNotContain("state-value-xyz", logger.All);
        Assert.DoesNotContain("verifier-secret-abc", logger.All);
        Assert.Contains("memory", logger.All, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Consume_InMemoryStateAfterItsLifetime_IsRefused()
    {
        using var redis = Unreachable();
        var clock = new MutableClock(Start);
        var store = new RedisGoogleOAuthStateStore(redis, clock, new ListLogger<RedisGoogleOAuthStateStore>());

        await store.TrySaveAsync("state-value-xyz", Payload, TimeSpan.FromMinutes(10), CancellationToken.None);
        clock.UtcNow = Start.AddMinutes(10).AddSeconds(1);

        Assert.Null(await store.TryConsumeAsync("state-value-xyz", CancellationToken.None));
    }

    [Fact]
    public async Task Consume_UnknownState_ReturnsNull_SoNoForgedCallbackCanSucceed()
    {
        using var redis = Unreachable();
        var store = new RedisGoogleOAuthStateStore(redis, new MutableClock(Start), new ListLogger<RedisGoogleOAuthStateStore>());

        await store.TrySaveAsync("state-value-xyz", Payload, TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Null(await store.TryConsumeAsync("some-other-state", CancellationToken.None));
    }

    [Fact]
    public async Task Save_InMemoryTableFullOfLiveStates_RefusesRatherThanGrowing()
    {
        using var redis = Unreachable();
        var store = new RedisGoogleOAuthStateStore(redis, new MutableClock(Start), new ListLogger<RedisGoogleOAuthStateStore>());

        for (var i = 0; i < RedisGoogleOAuthStateStore.MaxInMemoryStates; i++)
        {
            Assert.True(await store.TrySaveAsync($"state-{i}", Payload, TimeSpan.FromMinutes(10), CancellationToken.None));
        }

        Assert.False(await store.TrySaveAsync("one-too-many", Payload, TimeSpan.FromMinutes(10), CancellationToken.None));
    }

    [Fact]
    public async Task Save_InMemoryTableFullOfExpiredStates_MakesRoomForANewOne()
    {
        using var redis = Unreachable();
        var clock = new MutableClock(Start);
        var store = new RedisGoogleOAuthStateStore(redis, clock, new ListLogger<RedisGoogleOAuthStateStore>());

        for (var i = 0; i < RedisGoogleOAuthStateStore.MaxInMemoryStates; i++)
        {
            await store.TrySaveAsync($"state-{i}", Payload, TimeSpan.FromMinutes(10), CancellationToken.None);
        }

        clock.UtcNow = Start.AddMinutes(11);

        Assert.True(await store.TrySaveAsync("fresh", Payload, TimeSpan.FromMinutes(10), CancellationToken.None));
        Assert.Equal(Payload, await store.TryConsumeAsync("fresh", CancellationToken.None));
    }

    [Fact]
    public async Task Consume_EmptyState_ReturnsNullWithoutTouchingRedis()
    {
        using var redis = Unreachable();
        var store = new RedisGoogleOAuthStateStore(redis, new MutableClock(Start), new ListLogger<RedisGoogleOAuthStateStore>());

        Assert.Null(await store.TryConsumeAsync(string.Empty, CancellationToken.None));
    }
}
