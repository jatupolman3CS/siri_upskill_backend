using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Infrastructure.Delivery;

/// <summary>An <see cref="IClock"/> frozen at one instant — lets a claim stamp every row of a batch with the exact same, millisecond-exact value.</summary>
internal sealed class StampClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; } = utcNow;
}

/// <summary>What the relay needs to publish a claimed email row, plus what the row looked like before the claim (to put it back if the broker refuses).</summary>
internal sealed record ClaimedEmail(
    Guid Id,
    string? TemplateKey,
    int Attempts,
    EmailOutboxStatus PreviousStatus,
    DateTime? PreviousNextRetryAtUtc,
    DateTime? PreviousQueuedAtUtc);

/// <summary>A batch of email rows already marked <c>Queued</c> (and committed) by one relay cycle. <see cref="ClaimedAtUtc"/> is the stamp
/// they all carry in <c>QueuedAtUtc</c>, truncated to the column's millisecond precision so it can be compared for equality.</summary>
internal sealed record EmailClaim(DateTime ClaimedAtUtc, IReadOnlyList<ClaimedEmail> Items);

/// <summary>
/// Claims email-outbox rows for the relay in two steps that never hold a database lock across a network call:
/// <list type="number">
/// <item><see cref="ClaimAsync"/> — one short transaction: lock the due rows (<c>FOR UPDATE SKIP LOCKED</c>, so concurrent relays take different
/// rows), mark them <c>Queued</c>, <b>commit</b>. From that instant every other reader sees <c>Queued</c>: other relays skip them, and a consumer that
/// receives the record loads a committed, consistent row. (Publishing inside this transaction — the obvious alternative — lets a consumer read the
/// row before the relay commits, see its old state and then overwrite the relay's update; found by the end-to-end test.)</item>
/// <item><see cref="ReleaseAsync"/> — only for records the broker did not acknowledge: put those rows back as they were, so the next cycle retries.
/// If the process dies between the two steps the rows simply stay <c>Queued</c> until the stale window passes and they are claimed again.</item>
/// </list>
/// </summary>
internal interface IEmailOutboxRelayStore
{
    /// <param name="queuedStaleBeforeUtc">Rows still <c>Queued</c> since before this instant are claimed again (record lost, or its consumer died).
    /// <c>null</c> skips that sweep — used while the consumers are demonstrably making progress, so a long backlog is not mistaken for lost records.</param>
    Task<EmailClaim> ClaimAsync(int batchSize, DateTime nowUtc, DateTime? queuedStaleBeforeUtc, CancellationToken cancellationToken);

    /// <summary>Restores the rows in <paramref name="notPublished"/> to their pre-claim state — but only those still carrying this claim's stamp,
    /// checked and rewritten under a row lock so an outcome a consumer commits at the same moment can never be overwritten.</summary>
    Task ReleaseAsync(EmailClaim claim, IReadOnlyCollection<Guid> notPublished, CancellationToken cancellationToken);
}

/// <summary>
/// Rows claimed for one in-app relay cycle. Unlike the email path, the lock is held while publishing: the consumer of these events never reads
/// the notification row (it only drops a cache entry), so there is no stale-read hazard, and a failed publish simply rolls the transaction back.
/// Dispose without <see cref="CompleteAsync"/> rolls back and releases the rows untouched.
/// </summary>
internal interface IOutboxRelayBatch<out T> : IAsyncDisposable
{
    IReadOnlyList<T> Items { get; }

    /// <summary>Saves the changes made to <see cref="Items"/> (the "published" marks) and commits, releasing the locks.</summary>
    Task CompleteAsync(CancellationToken cancellationToken);
}

/// <summary>Claims in-app notifications whose creation event has not been published yet.</summary>
internal interface IInAppRelayStore
{
    Task<IOutboxRelayBatch<USER_NOTIFICATION>> BeginBatchAsync(
        int batchSize,
        DateTime createdAfterUtc,
        CancellationToken cancellationToken);

    /// <summary>Marks up to <paramref name="limit"/> unpublished notifications created before <paramref name="createdBeforeUtc"/> as published without
    /// an event (they are too old to matter — see <see cref="NotificationDeliveryOptions.InAppEventMaxAgeHours"/>). Bounded on purpose: the first run after
    /// enabling Kafka may face a long history, and one unbounded UPDATE could outlive the command timeout and be rolled back forever. Returns the row count;
    /// a result equal to <paramref name="limit"/> means there may be more.</summary>
    Task<int> MarkStaleUnpublishedAsync(DateTime createdBeforeUtc, DateTime nowUtc, int limit, CancellationToken cancellationToken);
}

internal sealed class EfOutboxRelayBatch<T>(AppDbContext dbContext, IDbContextTransaction transaction, IReadOnlyList<T> items)
    : IOutboxRelayBatch<T>
{
    public IReadOnlyList<T> Items => items;

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}

/// <summary>
/// Claim query for the email outbox. Raw SQL because EF has no row-lock hint; every identifier is quoted (database.md) and every
/// value is a parameter. The three due conditions mirror <see cref="EmailOutboxSenderJob"/>'s plus the stale-queued sweep:
/// brand-new rows, failed rows whose retry time has come, and rows handed to the broker long ago with no outcome recorded.
/// The first two are served by <c>IX_EMAIL_OUTBOX_STATUS_NEXT_RETRY_AT_UTC</c>; the third scans only the (normally tiny) set
/// of <c>Queued</c> rows through the same Status-leading index.
/// </summary>
internal sealed class EmailOutboxRelayStore(AppDbContext dbContext) : IEmailOutboxRelayStore
{
    /// <summary>Cuts a timestamp to whole milliseconds — the precision of the <c>QUEUED_AT_UTC</c> column — so the value written and the value compared back are identical.</summary>
    internal static DateTime TruncateToMilliseconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);

    public async Task<EmailClaim> ClaimAsync(
        int batchSize,
        DateTime nowUtc,
        DateTime? queuedStaleBeforeUtc,
        CancellationToken cancellationToken)
    {
        var claimedAtUtc = TruncateToMilliseconds(nowUtc);
        var stamp = new StampClock(claimedAtUtc);

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var pending = nameof(EmailOutboxStatus.Pending);
            var failed = nameof(EmailOutboxStatus.Failed);
            var queued = nameof(EmailOutboxStatus.Queued);

            var rows = await dbContext.EmailOutboxMessages()
                .FromSql($"""
                    SELECT * FROM "NOTIFY"."EMAIL_OUTBOX"
                    WHERE "STATUS" = {pending}
                       OR ("STATUS" = {failed} AND "NEXT_RETRY_AT_UTC" <= {nowUtc})
                       OR ("STATUS" = {queued} AND "QUEUED_AT_UTC" <= {queuedStaleBeforeUtc}) -- a NULL cutoff compares as unknown: no stale rows
                    ORDER BY "ID"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var items = new List<ClaimedEmail>(rows.Count);
            foreach (var row in rows)
            {
                items.Add(new ClaimedEmail(
                    row.Id,
                    row.TemplateKey,
                    row.Attempts,
                    row.Status,
                    row.NextRetryAtUtc,
                    row.QueuedAtUtc));
                row.MarkQueued(stamp);
            }

            if (rows.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            // The context lives for one relay cycle but is reused by ReleaseAsync: forget the entities so it re-reads whatever the database says by then.
            dbContext.ChangeTracker.Clear();

            return new EmailClaim(claimedAtUtc, items);
        }
    }

    public async Task ReleaseAsync(EmailClaim claim, IReadOnlyCollection<Guid> notPublished, CancellationToken cancellationToken)
    {
        if (notPublished.Count == 0)
        {
            return;
        }

        var ids = notPublished.ToArray();
        var claimedAtUtc = claim.ClaimedAtUtc;
        var previous = claim.Items.ToDictionary(i => i.Id);

        // Compare-and-swap, made atomic by locking the rows it inspects: only rows that still carry exactly this claim's stamp are selected, and
        // they stay locked until the revert commits. A consumer that commits an outcome first removes the row from the selection (the predicate is
        // re-evaluated after the lock wait); one that arrives later waits and then writes its own newer outcome over the revert. Either way a
        // recorded outcome is never overwritten — a plain "SELECT, then UPDATE by id" would leave a window for exactly that.
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var queued = nameof(EmailOutboxStatus.Queued);

            var rows = await dbContext.EmailOutboxMessages()
                .FromSql($"""
                    SELECT * FROM "NOTIFY"."EMAIL_OUTBOX"
                    WHERE "ID" = ANY({ids}) AND "STATUS" = {queued} AND "QUEUED_AT_UTC" = {claimedAtUtc}
                    FOR UPDATE
                    """)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in rows)
            {
                var before = previous[row.Id];
                row.RevertQueued(before.PreviousStatus, before.PreviousNextRetryAtUtc, before.PreviousQueuedAtUtc, claimedAtUtc);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class InAppRelayStore(AppDbContext dbContext) : IInAppRelayStore
{
    public async Task<IOutboxRelayBatch<USER_NOTIFICATION>> BeginBatchAsync(
        int batchSize,
        DateTime createdAfterUtc,
        CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Served by the partial index IX_NOTIFICATIONS_UNPUBLISHED ("PUBLISHED_AT_UTC" IS NULL).
            var rows = await dbContext.UserNotifications()
                .FromSql($"""
                    SELECT * FROM "NOTIFY"."NOTIFICATIONS"
                    WHERE "PUBLISHED_AT_UTC" IS NULL
                      AND "CREATED_AT_UTC" >= {createdAfterUtc}
                    ORDER BY "ID"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return new EfOutboxRelayBatch<USER_NOTIFICATION>(dbContext, transaction, rows);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<int> MarkStaleUnpublishedAsync(DateTime createdBeforeUtc, DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "NOTIFY"."NOTIFICATIONS" SET "PUBLISHED_AT_UTC" = {nowUtc}
            WHERE "ID" IN (
                SELECT "ID" FROM "NOTIFY"."NOTIFICATIONS"
                WHERE "PUBLISHED_AT_UTC" IS NULL AND "CREATED_AT_UTC" < {createdBeforeUtc}
                LIMIT {limit})
            """,
            cancellationToken);
}
