using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Features.MarkNotificationRead;
using Siri.Modules.Notification.Infrastructure;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using StackExchange.Redis;

namespace Siri.IntegrationTests;

/// <summary>
/// The Redis-backed parts of the notification pipeline against a real Redis-protocol server and a real PostgreSQL: the
/// duplicate-send guard, the per-minute send throttle and the unread-count cache (plus the fail-open behaviour of all three).
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class NotificationRedisStateIntegrationTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ServiceProvider _services = null!;
    private IConnectionMultiplexer _redis = null!;

    public NotificationRedisStateIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
                ["Redis:ConnectionString"] = _containers.RedisConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(configuration);
        services.AddSharedRedis(configuration);
        _services = services.BuildServiceProvider();
        _redis = _services.GetRequiredService<IConnectionMultiplexer>();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    // ---- duplicate-send guard ----

    private RedisEmailDeliveryGuard NewGuard() => new(_redis, NullLogger<RedisEmailDeliveryGuard>.Instance);

    [Fact]
    public async Task Guard_FirstClaimWins_ASecondCallerSeesItInFlight()
    {
        var guard = NewGuard();
        var id = Guid.NewGuid();

        var first = await guard.TryClaimAsync(id, CancellationToken.None);
        var second = await guard.TryClaimAsync(id, CancellationToken.None);

        Assert.Equal(DeliveryClaim.Claimed, first.Outcome);
        Assert.False(string.IsNullOrEmpty(first.Token));
        Assert.Equal(DeliveryClaim.InFlightElsewhere, second.Outcome);
        Assert.Null(second.Token);
    }

    [Fact]
    public async Task Guard_AfterMarkDelivered_EveryLaterClaimIsAlreadyDelivered()
    {
        var guard = NewGuard();
        var id = Guid.NewGuid();

        await guard.TryClaimAsync(id, CancellationToken.None);
        await guard.MarkDeliveredAsync(id, CancellationToken.None);

        Assert.Equal(DeliveryClaim.AlreadyDelivered, (await guard.TryClaimAsync(id, CancellationToken.None)).Outcome);
        Assert.Equal(DeliveryClaim.AlreadyDelivered, (await guard.TryClaimAsync(id, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Guard_ReleaseWithTheOwnersToken_LetsTheRetryClaimAgain()
    {
        var guard = NewGuard();
        var id = Guid.NewGuid();

        var ticket = await guard.TryClaimAsync(id, CancellationToken.None);
        await guard.ReleaseAsync(id, ticket.Token, CancellationToken.None);

        Assert.Equal(DeliveryClaim.Claimed, (await guard.TryClaimAsync(id, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Guard_AHolderWhoseClaimExpired_CannotReleaseTheClaimOfWhoeverTookOver()
    {
        var guard = NewGuard();
        var id = Guid.NewGuid();

        var slowHolder = await guard.TryClaimAsync(id, CancellationToken.None);
        await _redis.GetDatabase().KeyDeleteAsync(RedisEmailDeliveryGuard.KeyFor(id)); // the slow holder's claim lapses
        var newHolder = await guard.TryClaimAsync(id, CancellationToken.None);
        Assert.Equal(DeliveryClaim.Claimed, newHolder.Outcome);

        await guard.ReleaseAsync(id, slowHolder.Token, CancellationToken.None); // the slow holder finally gives up

        // The new holder still owns it: a third consumer must be told it is in flight, not handed a second claim (which would double-send).
        Assert.Equal(DeliveryClaim.InFlightElsewhere, (await guard.TryClaimAsync(id, CancellationToken.None)).Outcome);
        await guard.ReleaseAsync(id, newHolder.Token, CancellationToken.None);
        Assert.Equal(DeliveryClaim.Claimed, (await guard.TryClaimAsync(id, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Guard_ReleaseWithoutAToken_DoesNothing()
    {
        var guard = NewGuard();
        var id = Guid.NewGuid();
        await guard.TryClaimAsync(id, CancellationToken.None);

        await guard.ReleaseAsync(id, token: null, CancellationToken.None);

        Assert.Equal(DeliveryClaim.InFlightElsewhere, (await guard.TryClaimAsync(id, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Guard_ReleaseNeverErasesADeliveredMarker()
    {
        var guard = NewGuard();
        var id = Guid.NewGuid();

        var ticket = await guard.TryClaimAsync(id, CancellationToken.None);
        await guard.MarkDeliveredAsync(id, CancellationToken.None);
        await guard.ReleaseAsync(id, ticket.Token, CancellationToken.None);

        Assert.Equal(DeliveryClaim.AlreadyDelivered, (await guard.TryClaimAsync(id, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Guard_ClaimsExpire_SoACrashedHolderCannotBlockAMessageForever()
    {
        var id = Guid.NewGuid();
        var guard = NewGuard();

        await guard.TryClaimAsync(id, CancellationToken.None);

        var ttl = await _redis.GetDatabase().KeyTimeToLiveAsync(RedisEmailDeliveryGuard.KeyFor(id));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromSeconds(1), RedisEmailDeliveryGuard.ClaimLifetime);
    }

    [Fact]
    public async Task Guard_DeliveredMarkerOutlivesTheRelaysStaleWindow()
    {
        var id = Guid.NewGuid();
        var guard = NewGuard();

        await guard.MarkDeliveredAsync(id, CancellationToken.None);

        var ttl = await _redis.GetDatabase().KeyTimeToLiveAsync(RedisEmailDeliveryGuard.KeyFor(id));
        Assert.NotNull(ttl);
        Assert.True(ttl.Value > TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task Guard_ManyConsumersRaceForOneMessage_ExactlyOneWins()
    {
        var guard = NewGuard();
        var id = Guid.NewGuid();

        var tickets = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => guard.TryClaimAsync(id, CancellationToken.None)));

        Assert.Single(tickets, t => t.Outcome == DeliveryClaim.Claimed);
        Assert.Equal(19, tickets.Count(t => t.Outcome == DeliveryClaim.InFlightElsewhere));
    }

    [Fact]
    public async Task Guard_RedisUnreachable_FailsOpenAndDeliversAnyway()
    {
        using var dead = await ConnectionMultiplexer.ConnectAsync(
            "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200,asyncTimeout=200");
        var guard = new RedisEmailDeliveryGuard(dead, NullLogger<RedisEmailDeliveryGuard>.Instance);
        var id = Guid.NewGuid();

        var ticket = await guard.TryClaimAsync(id, CancellationToken.None);

        Assert.Equal(DeliveryClaim.Claimed, ticket.Outcome);
        Assert.Null(ticket.Token); // nothing was actually claimed, so there is nothing to release
        await guard.MarkDeliveredAsync(id, CancellationToken.None); // must not throw
        await guard.ReleaseAsync(id, "any", CancellationToken.None); // must not throw
    }

    // ---- consumer heartbeat ----

    private RedisEmailConsumerHeartbeat NewHeartbeat(string prefix, IClock clock, IConnectionMultiplexer? redis = null) =>
        new(redis ?? _redis, NotificationTopics.For(prefix), clock, NullLogger<RedisEmailConsumerHeartbeat>.Instance);

    private static string UniquePrefix() => $"siriupskill-it-hb-{Guid.NewGuid():N}";

    [Fact]
    public async Task Heartbeat_NeverBeat_MeansNoProgress()
    {
        var heartbeat = NewHeartbeat(UniquePrefix(), new FixedClock(DateTime.UtcNow));

        Assert.False(await heartbeat.WasActiveWithinAsync(TimeSpan.FromMinutes(15), CancellationToken.None));
    }

    [Fact]
    public async Task Heartbeat_RecentBeat_MeansTheConsumersAreWorking()
    {
        var clock = new FixedClock(DateTime.UtcNow);
        var heartbeat = NewHeartbeat(UniquePrefix(), clock);

        await heartbeat.BeatAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddMinutes(10);

        Assert.True(await heartbeat.WasActiveWithinAsync(TimeSpan.FromMinutes(15), CancellationToken.None));
    }

    [Fact]
    public async Task Heartbeat_BeatOlderThanTheWindow_MeansTheConsumersWentQuiet()
    {
        var clock = new FixedClock(DateTime.UtcNow);
        var heartbeat = NewHeartbeat(UniquePrefix(), clock);

        await heartbeat.BeatAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddMinutes(16);

        Assert.False(await heartbeat.WasActiveWithinAsync(TimeSpan.FromMinutes(15), CancellationToken.None));
    }

    [Fact]
    public async Task Heartbeat_IsPerConsumerGroup_SoEnvironmentsSharingARedisNeverSeeEachOthersProgress()
    {
        var clock = new FixedClock(DateTime.UtcNow);
        var prodLike = NewHeartbeat(UniquePrefix(), clock);
        var devLike = NewHeartbeat(UniquePrefix(), clock);

        await prodLike.BeatAsync(CancellationToken.None);

        Assert.True(await prodLike.WasActiveWithinAsync(TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.False(await devLike.WasActiveWithinAsync(TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task Heartbeat_ManyBeatsInASecond_WriteRedisOnlyOnce()
    {
        var clock = new FixedClock(DateTime.UtcNow);
        var prefix = UniquePrefix();
        var heartbeat = NewHeartbeat(prefix, clock);

        await heartbeat.BeatAsync(CancellationToken.None);
        var firstStamp = (long?)await _redis.GetDatabase().StringGetAsync($"notify:email:heartbeat:{NotificationTopics.For(prefix).EmailGroup}");

        clock.UtcNow = clock.UtcNow.AddMilliseconds(400);
        await heartbeat.BeatAsync(CancellationToken.None);
        var secondStamp = (long?)await _redis.GetDatabase().StringGetAsync($"notify:email:heartbeat:{NotificationTopics.For(prefix).EmailGroup}");

        Assert.NotNull(firstStamp);
        Assert.Equal(firstStamp, secondStamp);
    }

    [Fact]
    public async Task Heartbeat_RedisUnreachable_FailsOpenToNoProgressSoTheSweepStillRuns()
    {
        using var dead = await ConnectionMultiplexer.ConnectAsync(
            "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200,asyncTimeout=200");
        var heartbeat = NewHeartbeat(UniquePrefix(), new FixedClock(DateTime.UtcNow), dead);

        await heartbeat.BeatAsync(CancellationToken.None); // must not throw
        Assert.False(await heartbeat.WasActiveWithinAsync(TimeSpan.FromMinutes(15), CancellationToken.None));
    }

    // ---- per-minute send throttle ----

    private static DateTime UniqueMinuteJustBeforeTheBoundary()
    {
        // A minute in the far future that no other run uses (the counter key is derived from the clock), 100 ms before it ends.
        var random = Random.Shared;
        return new DateTime(2099, random.Next(1, 13), random.Next(1, 29), random.Next(0, 24), random.Next(0, 60), 59, 900, DateTimeKind.Utc);
    }

    private RedisEmailSendThrottle NewThrottle(int perMinute, IClock clock) =>
        new(_redis, Options.Create(new NotificationDeliveryOptions { MaxEmailsPerMinute = perMinute }), clock, NullLogger<RedisEmailSendThrottle>.Instance);

    [Fact]
    public async Task Throttle_UnderTheLimit_NeverWaits()
    {
        var throttle = NewThrottle(5, new FixedClock(UniqueMinuteJustBeforeTheBoundary()));

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
        {
            await throttle.WaitForSlotAsync(CancellationToken.None);
        }

        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250), $"5 sends under a limit of 5 took {watch.Elapsed}");
    }

    [Fact]
    public async Task Throttle_OverTheLimit_WaitsForTheNextWindow()
    {
        var throttle = NewThrottle(2, new FixedClock(UniqueMinuteJustBeforeTheBoundary()));

        await throttle.WaitForSlotAsync(CancellationToken.None);
        await throttle.WaitForSlotAsync(CancellationToken.None);

        var watch = Stopwatch.StartNew();
        await throttle.WaitForSlotAsync(CancellationToken.None); // the third in the same minute

        // 100 ms to the boundary + the 250 ms margin, per window waited through (the fixed clock never leaves the window, so it waits all 3 then proceeds).
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(300), $"expected a wait, got {watch.Elapsed}");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Throttle_TwoInstancesShareOneBudget()
    {
        var clock = new FixedClock(UniqueMinuteJustBeforeTheBoundary());
        var first = NewThrottle(2, clock);
        var second = NewThrottle(2, clock);

        await first.WaitForSlotAsync(CancellationToken.None);
        await second.WaitForSlotAsync(CancellationToken.None);

        var watch = Stopwatch.StartNew();
        await first.WaitForSlotAsync(CancellationToken.None);

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(300), "the budget of 2 was already spent across both instances");
    }

    [Fact]
    public async Task Throttle_ZeroMeansUnlimited()
    {
        var throttle = NewThrottle(0, new FixedClock(UniqueMinuteJustBeforeTheBoundary()));

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            await throttle.WaitForSlotAsync(CancellationToken.None);
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Throttle_RedisUnreachable_FailsOpen()
    {
        using var dead = await ConnectionMultiplexer.ConnectAsync(
            "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200,asyncTimeout=200");
        var throttle = new RedisEmailSendThrottle(
            dead,
            Options.Create(new NotificationDeliveryOptions { MaxEmailsPerMinute = 1 }),
            new FixedClock(UniqueMinuteJustBeforeTheBoundary()),
            NullLogger<RedisEmailSendThrottle>.Instance);

        await throttle.WaitForSlotAsync(CancellationToken.None); // must return, not throw
    }

    [Fact]
    public async Task Throttle_CounterExpiresOnItsOwn()
    {
        var clock = new FixedClock(UniqueMinuteJustBeforeTheBoundary());
        var throttle = NewThrottle(10, clock);

        await throttle.WaitForSlotAsync(CancellationToken.None);

        var (key, _) = RedisEmailSendThrottle.WindowFor(clock.UtcNow);
        var ttl = await _redis.GetDatabase().KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.True(ttl.Value <= TimeSpan.FromMinutes(2));
    }

    // ---- unread count cache ----

    private async Task<UnreadFixture> NewUnreadFixtureAsync(int cacheSeconds = 600, IConnectionMultiplexer? redis = null)
    {
        var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = new UserNotificationRepository(db);
        var counter = new UnreadNotificationCounter(
            redis ?? _redis,
            repository,
            Options.Create(new NotificationDeliveryOptions { UnreadCountCacheSeconds = cacheSeconds }),
            NullLogger<UnreadNotificationCounter>.Instance);
        return new UnreadFixture(scope, db, counter, Guid.NewGuid());
    }

    private sealed class UnreadFixture(AsyncServiceScope scope, AppDbContext db, UnreadNotificationCounter counter, Guid userId) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;

        public UnreadNotificationCounter Counter { get; } = counter;

        public Guid UserId { get; } = userId;

        public async Task<USER_NOTIFICATION> AddAsync()
        {
            var notification = USER_NOTIFICATION.Create(UserId, "count.test", "Title", "Body", null, new FixedClock(DateTime.UtcNow));
            Db.UserNotifications().Add(notification);
            await Db.SaveChangesAsync();
            return notification;
        }

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    [Fact]
    public async Task Unread_FirstReadCountsFromTheDatabase_ThenServesFromCacheUntilInvalidated()
    {
        await using var f = await NewUnreadFixtureAsync();
        await f.AddAsync();
        await f.AddAsync();

        Assert.Equal(2, await f.Counter.GetAsync(f.UserId, CancellationToken.None));

        await f.AddAsync(); // a third arrives, but nothing has told the cache
        Assert.Equal(2, await f.Counter.GetAsync(f.UserId, CancellationToken.None));

        await f.Counter.InvalidateAsync(f.UserId, CancellationToken.None);
        Assert.Equal(3, await f.Counter.GetAsync(f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Unread_NoNotifications_IsZeroAndCachedAsZero()
    {
        await using var f = await NewUnreadFixtureAsync();

        Assert.Equal(0, await f.Counter.GetAsync(f.UserId, CancellationToken.None));
        Assert.Equal("0", (string?)await _redis.GetDatabase().StringGetAsync(UnreadNotificationCounter.KeyFor(f.UserId)));
    }

    [Fact]
    public async Task Unread_CachedValueExpiresAfterTheConfiguredTtl()
    {
        await using var f = await NewUnreadFixtureAsync(cacheSeconds: 1);
        await f.AddAsync();
        Assert.Equal(1, await f.Counter.GetAsync(f.UserId, CancellationToken.None));

        await f.AddAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        Assert.Equal(2, await f.Counter.GetAsync(f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Unread_RedisUnreachable_StillAnswersFromTheDatabase()
    {
        using var dead = await ConnectionMultiplexer.ConnectAsync(
            "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200,asyncTimeout=200");
        await using var f = await NewUnreadFixtureAsync(redis: dead);
        await f.AddAsync();
        await f.AddAsync();

        Assert.Equal(2, await f.Counter.GetAsync(f.UserId, CancellationToken.None));
        await f.Counter.InvalidateAsync(f.UserId, CancellationToken.None); // must not throw
    }

    [Fact]
    public async Task Unread_OnlyCountsTheCallersOwnUnreadNotifications()
    {
        await using var mine = await NewUnreadFixtureAsync();
        await using var theirs = await NewUnreadFixtureAsync();
        await mine.AddAsync();
        await theirs.AddAsync();
        await theirs.AddAsync();
        var read = await mine.AddAsync();
        read.MarkRead(new FixedClock(DateTime.UtcNow));
        await mine.Db.SaveChangesAsync();

        Assert.Equal(1, await mine.Counter.GetAsync(mine.UserId, CancellationToken.None));
        Assert.Equal(2, await theirs.Counter.GetAsync(theirs.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task MarkRead_DropsTheCachedCountSoTheBadgeUpdatesAtOnce()
    {
        await using var f = await NewUnreadFixtureAsync();
        var first = await f.AddAsync();
        await f.AddAsync();
        Assert.Equal(2, await f.Counter.GetAsync(f.UserId, CancellationToken.None));

        var handler = new MarkNotificationReadHandler(f.Db, new FixedClock(DateTime.UtcNow), f.Counter);
        var result = await handler.HandleAsync(f.UserId, first.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await f.Counter.GetAsync(f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task MarkRead_SomeoneElsesNotification_IsForbiddenAndLeavesTheirCountAlone()
    {
        await using var owner = await NewUnreadFixtureAsync();
        await using var intruder = await NewUnreadFixtureAsync();
        var notification = await owner.AddAsync();
        Assert.Equal(1, await owner.Counter.GetAsync(owner.UserId, CancellationToken.None));

        var handler = new MarkNotificationReadHandler(intruder.Db, new FixedClock(DateTime.UtcNow), intruder.Counter);
        var result = await handler.HandleAsync(intruder.UserId, notification.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, await owner.Counter.GetAsync(owner.UserId, CancellationToken.None));
    }
}
