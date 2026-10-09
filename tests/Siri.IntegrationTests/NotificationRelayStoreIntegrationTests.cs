using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// The relay's claim queries against a real PostgreSQL: which rows are due, that two relays never claim the same row
/// (<c>FOR UPDATE SKIP LOCKED</c>), and that a claim only becomes durable when the batch is completed. The table is shared with other
/// test classes, so every assertion is about the rows this class created (identified by id), never about global counts.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class NotificationRelayStoreIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StaleBefore = Now.AddMinutes(-15);

    private readonly ContainersFixture _containers;
    private ServiceProvider _services = null!;

    public NotificationRelayStoreIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = _containers.SqlConnectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddPersistence(configuration);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    private static EMAIL_OUTBOX_MESSAGE NewEmail() =>
        EMAIL_OUTBOX_MESSAGE.Enqueue($"relay-{Guid.NewGuid():N}@example.test", "Subject", "<p>Body</p>", "relay-test");

    private async Task SaveAsync(params EMAIL_OUTBOX_MESSAGE[] messages)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.EmailOutboxMessages().AddRange(messages);
        await db.SaveChangesAsync();
    }

    private async Task<EMAIL_OUTBOX_MESSAGE> ReloadAsync(Guid id)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .EmailOutboxMessages().AsNoTracking().SingleAsync(m => m.Id == id);
    }

    [Fact]
    public async Task Claim_TakesPendingRetryDueAndStaleQueuedRows_AndNothingElse()
    {
        var pending = NewEmail();

        var retryDue = NewEmail();
        retryDue.RecordAttemptFailed("down", new FixedClock(Now.AddMinutes(-10))); // retry was due at Now-9min

        var retryLater = NewEmail();
        retryLater.RecordAttemptFailed("down", new FixedClock(Now)); // retry due at Now+1min

        var exhausted = NewEmail();
        for (var i = 0; i < EMAIL_OUTBOX_MESSAGE.MaxAttempts; i++)
        {
            exhausted.RecordAttemptFailed("down", new FixedClock(Now.AddHours(-1)));
        }

        var sent = NewEmail();
        sent.RecordSent(new FixedClock(Now.AddMinutes(-30)));

        var queuedFresh = NewEmail();
        queuedFresh.MarkQueued(new FixedClock(Now.AddMinutes(-1)));

        var queuedStale = NewEmail();
        queuedStale.MarkQueued(new FixedClock(Now.AddMinutes(-20)));

        await SaveAsync(pending, retryDue, retryLater, exhausted, sent, queuedFresh, queuedStale);

        EmailClaim claim;
        await using (var scope = _services.CreateAsyncScope())
        {
            claim = await new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>())
                .ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);
        }

        var claimed = claim.Items.Select(i => i.Id).ToHashSet();
        Assert.Contains(pending.Id, claimed);
        Assert.Contains(retryDue.Id, claimed);
        Assert.Contains(queuedStale.Id, claimed);
        Assert.DoesNotContain(retryLater.Id, claimed);
        Assert.DoesNotContain(exhausted.Id, claimed);
        Assert.DoesNotContain(sent.Id, claimed);
        Assert.DoesNotContain(queuedFresh.Id, claimed);
    }

    [Fact]
    public async Task Claim_MarksTheRowsQueuedAndCommitsItBeforeAnythingIsPublished()
    {
        var message = NewEmail();
        await SaveAsync(message);

        EmailClaim claim;
        await using (var scope = _services.CreateAsyncScope())
        {
            claim = await new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>())
                .ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);
        }

        // Read through a different connection with the claiming context already gone: what a consumer would see.
        var seenByAConsumer = await ReloadAsync(message.Id);
        Assert.Equal(EmailOutboxStatus.Queued, seenByAConsumer.Status);
        Assert.Equal(claim.ClaimedAtUtc, seenByAConsumer.QueuedAtUtc);
        Assert.Equal(claim.ClaimedAtUtc, EmailOutboxRelayStore.TruncateToMilliseconds(claim.ClaimedAtUtc));
    }

    [Fact]
    public async Task Claim_RecordsWhatEachRowLookedLikeBeforeSoItCanBePutBack()
    {
        var pending = NewEmail();
        var retryDue = NewEmail();
        retryDue.RecordAttemptFailed("down", new FixedClock(Now.AddMinutes(-10)));
        var dueAt = retryDue.NextRetryAtUtc;
        await SaveAsync(pending, retryDue);

        EmailClaim claim;
        await using (var scope = _services.CreateAsyncScope())
        {
            claim = await new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>())
                .ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);
        }

        var before = claim.Items.Single(i => i.Id == pending.Id);
        Assert.Equal(EmailOutboxStatus.Pending, before.PreviousStatus);
        Assert.Null(before.PreviousNextRetryAtUtc);

        var beforeRetry = claim.Items.Single(i => i.Id == retryDue.Id);
        Assert.Equal(EmailOutboxStatus.Failed, beforeRetry.PreviousStatus);
        Assert.Equal(1, beforeRetry.Attempts);
        Assert.NotNull(beforeRetry.PreviousNextRetryAtUtc);
        Assert.Equal(dueAt!.Value, beforeRetry.PreviousNextRetryAtUtc!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Claim_BatchSize_LimitsHowManyRowsAreClaimed()
    {
        await SaveAsync(NewEmail(), NewEmail(), NewEmail());

        await using var scope = _services.CreateAsyncScope();
        var claim = await new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>())
            .ClaimAsync(2, Now, StaleBefore, CancellationToken.None);

        Assert.Equal(2, claim.Items.Count);
    }

    [Fact]
    public async Task Claim_ManyRelaysAtOnce_NeverTakeTheSameRowTwice()
    {
        var mine = Enumerable.Range(0, 12).Select(_ => NewEmail()).ToArray();
        await SaveAsync(mine);
        var mineIds = mine.Select(m => m.Id).ToHashSet();

        // Four relays claim at the very same moment, each wanting more rows than exist.
        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = _services.CreateAsyncScope();
            return await new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>())
                .ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);
        })).WaitAsync(TimeSpan.FromSeconds(30));

        var allClaimedIds = claims.SelectMany(c => c.Items.Select(i => i.Id)).ToList();

        Assert.Equal(allClaimedIds.Count, allClaimedIds.Distinct().Count()); // no row appears in two claims
        Assert.Subset(allClaimedIds.ToHashSet(), mineIds); // and every one of mine was claimed by somebody
    }

    [Fact]
    public async Task Claim_AlreadyClaimedRows_AreNotClaimedAgainBySomeoneElse()
    {
        var message = NewEmail();
        await SaveAsync(message);

        await using var scopeA = _services.CreateAsyncScope();
        await using var scopeB = _services.CreateAsyncScope();
        var first = await new EmailOutboxRelayStore(scopeA.ServiceProvider.GetRequiredService<AppDbContext>())
            .ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);
        var second = await new EmailOutboxRelayStore(scopeB.ServiceProvider.GetRequiredService<AppDbContext>())
            .ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);

        Assert.Contains(first.Items, i => i.Id == message.Id);
        Assert.DoesNotContain(second.Items, i => i.Id == message.Id);
    }

    [Fact]
    public async Task Release_PutsAPendingRowBackSoTheNextCycleClaimsItAgain()
    {
        var message = NewEmail();
        await SaveAsync(message);

        await using (var scope = _services.CreateAsyncScope())
        {
            var store = new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            var claim = await store.ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);
            await store.ReleaseAsync(claim, [message.Id], CancellationToken.None);
        }

        var restored = await ReloadAsync(message.Id);
        Assert.Equal(EmailOutboxStatus.Pending, restored.Status);
        Assert.Null(restored.QueuedAtUtc);

        await using var scope2 = _services.CreateAsyncScope();
        var again = await new EmailOutboxRelayStore(scope2.ServiceProvider.GetRequiredService<AppDbContext>())
            .ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);
        Assert.Contains(again.Items, i => i.Id == message.Id);
    }

    [Fact]
    public async Task Release_PutsARetryRowBackWithItsRetryTimeAndAttempts()
    {
        var message = NewEmail();
        message.RecordAttemptFailed("down", new FixedClock(Now.AddMinutes(-10)));
        var dueAt = message.NextRetryAtUtc!.Value;
        await SaveAsync(message);

        await using (var scope = _services.CreateAsyncScope())
        {
            var store = new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            var claim = await store.ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);
            await store.ReleaseAsync(claim, [message.Id], CancellationToken.None);
        }

        var restored = await ReloadAsync(message.Id);
        Assert.Equal(EmailOutboxStatus.Failed, restored.Status);
        Assert.Equal(1, restored.Attempts);
        Assert.Equal(dueAt, restored.NextRetryAtUtc!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Release_OnlyTouchesTheRowsItWasToldAbout()
    {
        var published = NewEmail();
        var refused = NewEmail();
        await SaveAsync(published, refused);

        await using var scope = _services.CreateAsyncScope();
        var store = new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        var claim = await store.ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);
        await store.ReleaseAsync(claim, [refused.Id], CancellationToken.None);

        Assert.Equal(EmailOutboxStatus.Queued, (await ReloadAsync(published.Id)).Status);
        Assert.Equal(EmailOutboxStatus.Pending, (await ReloadAsync(refused.Id)).Status);
    }

    [Fact]
    public async Task Release_NeverOverwritesAnOutcomeAConsumerAlreadyRecorded()
    {
        var message = NewEmail();
        await SaveAsync(message);

        await using var scope = _services.CreateAsyncScope();
        var store = new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        var claim = await store.ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);

        // The broker's acknowledgement was lost (a timeout), but the record had arrived: a consumer delivers the mail and records it.
        await using (var consumerScope = _services.CreateAsyncScope())
        {
            var db = consumerScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.EmailOutboxMessages().SingleAsync(m => m.Id == message.Id);
            row.RecordSent(new FixedClock(Now.AddSeconds(2)));
            await db.SaveChangesAsync();
        }

        await store.ReleaseAsync(claim, [message.Id], CancellationToken.None);

        Assert.Equal(EmailOutboxStatus.Sent, (await ReloadAsync(message.Id)).Status);
    }

    [Fact]
    public async Task Release_AfterALaterClaimTookTheRow_LeavesItAlone()
    {
        var message = NewEmail();
        await SaveAsync(message);

        await using var scope = _services.CreateAsyncScope();
        var store = new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        var firstClaim = await store.ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);

        // Much later the row is stale and a second claim (a different stamp) takes it.
        await using var scope2 = _services.CreateAsyncScope();
        var later = Now.AddHours(1);
        var secondClaim = await new EmailOutboxRelayStore(scope2.ServiceProvider.GetRequiredService<AppDbContext>())
            .ClaimAsync(1000, later, later.AddMinutes(-15), CancellationToken.None);
        Assert.Contains(secondClaim.Items, i => i.Id == message.Id);

        await store.ReleaseAsync(firstClaim, [message.Id], CancellationToken.None);

        var row = await ReloadAsync(message.Id);
        Assert.Equal(EmailOutboxStatus.Queued, row.Status);
        Assert.Equal(secondClaim.ClaimedAtUtc, row.QueuedAtUtc);
    }

    // ---- in-app notifications ----

    private async Task SaveAsync(params USER_NOTIFICATION[] notifications)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserNotifications().AddRange(notifications);
        await db.SaveChangesAsync();
    }

    private static USER_NOTIFICATION NewNotification(Guid userId, DateTime createdAtUtc) =>
        USER_NOTIFICATION.Create(userId, "relay.test", "Title", "Body", "/my-courses", new FixedClock(createdAtUtc));

    [Fact]
    public async Task InAppBatch_ClaimsOnlyFreshUnpublishedNotifications()
    {
        var userId = Guid.NewGuid();
        var fresh = NewNotification(userId, Now.AddMinutes(-5));
        var old = NewNotification(userId, Now.AddDays(-3));
        var published = NewNotification(userId, Now.AddMinutes(-4));
        published.MarkPublished(new FixedClock(Now.AddMinutes(-3)));
        await SaveAsync(fresh, old, published);

        await using var scope = _services.CreateAsyncScope();
        var store = new InAppRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        await using var batch = await store.BeginBatchAsync(1000, createdAfterUtc: Now.AddHours(-24), CancellationToken.None);

        var claimed = batch.Items.Select(n => n.Id).ToHashSet();
        Assert.Contains(fresh.Id, claimed);
        Assert.DoesNotContain(old.Id, claimed);
        Assert.DoesNotContain(published.Id, claimed);
    }

    [Fact]
    public async Task InAppBatch_CompleteAsync_MarksThemPublishedSoTheyAreNotClaimedAgain()
    {
        var notification = NewNotification(Guid.NewGuid(), Now.AddMinutes(-1));
        await SaveAsync(notification);

        await using (var scope = _services.CreateAsyncScope())
        {
            var store = new InAppRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            await using var batch = await store.BeginBatchAsync(1000, Now.AddHours(-24), CancellationToken.None);
            batch.Items.Single(n => n.Id == notification.Id).MarkPublished(new FixedClock(Now));
            await batch.CompleteAsync(CancellationToken.None);
        }

        await using var scope2 = _services.CreateAsyncScope();
        var store2 = new InAppRelayStore(scope2.ServiceProvider.GetRequiredService<AppDbContext>());
        await using var batch2 = await store2.BeginBatchAsync(1000, Now.AddHours(-24), CancellationToken.None);
        Assert.DoesNotContain(batch2.Items, n => n.Id == notification.Id);
    }

    [Fact]
    public async Task InAppStore_MarkStaleUnpublished_RetiresOnlyTheOldOnes()
    {
        var userId = Guid.NewGuid();
        var old = NewNotification(userId, Now.AddDays(-3));
        var fresh = NewNotification(userId, Now.AddMinutes(-1));
        await SaveAsync(old, fresh);

        await using (var scope = _services.CreateAsyncScope())
        {
            var store = new InAppRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            var retired = await store.MarkStaleUnpublishedAsync(Now.AddHours(-24), Now, 1000, CancellationToken.None);
            Assert.True(retired >= 1);
        }

        await using var verify = _services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull((await db.UserNotifications().AsNoTracking().SingleAsync(n => n.Id == old.Id)).PublishedAtUtc);
        Assert.Null((await db.UserNotifications().AsNoTracking().SingleAsync(n => n.Id == fresh.Id)).PublishedAtUtc);
    }

    [Fact]
    public async Task InAppStore_MarkStaleUnpublished_NeverRetiresMoreThanTheLimitInOneCall()
    {
        var userId = Guid.NewGuid();
        var old = Enumerable.Range(0, 5).Select(_ => NewNotification(userId, Now.AddDays(-5))).ToArray();
        await SaveAsync(old);
        var oldIds = old.Select(n => n.Id).ToList();

        await using (var scope = _services.CreateAsyncScope())
        {
            var store = new InAppRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            var retired = await store.MarkStaleUnpublishedAsync(Now.AddHours(-24), Now, 2, CancellationToken.None);
            Assert.True(retired <= 2, $"asked for at most 2, retired {retired}");
        }

        await using var verify = _services.CreateAsyncScope();
        var rows = await verify.ServiceProvider.GetRequiredService<AppDbContext>().UserNotifications()
            .AsNoTracking().Where(n => oldIds.Contains(n.Id)).ToListAsync();
        Assert.True(rows.Count(n => n.PublishedAtUtc is not null) <= 2);
    }

    [Fact]
    public async Task Claim_WithoutAStaleCutoff_LeavesRowsThatMerelyWaitInKafkaAlone()
    {
        var pending = NewEmail();
        var waitingLong = NewEmail();
        waitingLong.MarkQueued(new FixedClock(Now.AddHours(-2)));
        await SaveAsync(pending, waitingLong);

        await using var scope = _services.CreateAsyncScope();
        var claim = await new EmailOutboxRelayStore(scope.ServiceProvider.GetRequiredService<AppDbContext>())
            .ClaimAsync(1000, Now, queuedStaleBeforeUtc: null, CancellationToken.None);

        Assert.Contains(claim.Items, i => i.Id == pending.Id);
        Assert.DoesNotContain(claim.Items, i => i.Id == waitingLong.Id);
        Assert.Equal(EmailOutboxStatus.Queued, (await ReloadAsync(waitingLong.Id)).Status);
    }

    [Fact]
    public async Task Release_WhileAConsumerIsRecordingItsOutcome_WaitsForItAndNeverOverwritesIt()
    {
        var message = NewEmail();
        await SaveAsync(message);

        await using var relayScope = _services.CreateAsyncScope();
        var store = new EmailOutboxRelayStore(relayScope.ServiceProvider.GetRequiredService<AppDbContext>());
        var claim = await store.ClaimAsync(1000, Now, StaleBefore, CancellationToken.None);

        // The broker's acknowledgement was lost, but the record arrived and a consumer is in the middle of committing "Sent"
        // (its UPDATE holds the row lock; the transaction has not committed yet).
        await using var consumerScope = _services.CreateAsyncScope();
        var consumerDb = consumerScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var consumerTransaction = await consumerDb.Database.BeginTransactionAsync();
        var row = await consumerDb.EmailOutboxMessages().SingleAsync(m => m.Id == message.Id);
        row.RecordSent(new FixedClock(Now.AddSeconds(2)));
        await consumerDb.SaveChangesAsync();

        // The relay now releases that row. It must wait for the consumer's lock rather than read the stale Queued state and write Pending over Sent.
        var release = store.ReleaseAsync(claim, [message.Id], CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        Assert.False(release.IsCompleted, "the release should be waiting on the consumer's row lock");

        await consumerTransaction.CommitAsync();
        await release.WaitAsync(TimeSpan.FromSeconds(15));

        var final = await ReloadAsync(message.Id);
        Assert.Equal(EmailOutboxStatus.Sent, final.Status);
        Assert.NotNull(final.SentAtUtc);
    }
}
