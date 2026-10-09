using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary>
/// Reads the outbox's health for operators. Every query is read-only (<c>AsNoTracking</c>) and bounded: the counts are capped at
/// <see cref="IEmailOutboxHealthReader.CountCap"/> and both use <c>IX_EMAIL_OUTBOX_STATUS_NEXT_RETRY_AT_UTC</c> (Status leads the index). There is no
/// created-at column on the outbox, so the queue time of the oldest waiting message is read from its UUIDv7 key — the same property the sender job
/// already relies on to send oldest-first (<see cref="UuidV7"/>).
/// </summary>
public sealed class EmailOutboxHealthReader(AppDbContext dbContext) : IEmailOutboxHealthReader
{
    public async Task<EmailOutboxHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        // Waiting = will still be delivered: never tried (Pending), handed to the broker and not yet consumed (Queued — only with the Kafka
        // transport), or failed with a retry scheduled.
        var waiting = dbContext.EmailOutboxMessages()
            .AsNoTracking()
            .Where(m => m.Status == EmailOutboxStatus.Pending
                || m.Status == EmailOutboxStatus.Queued
                || (m.Status == EmailOutboxStatus.Failed && m.NextRetryAtUtc != null));

        var waitingCount = await waiting.Take(IEmailOutboxHealthReader.CountCap).CountAsync(cancellationToken).ConfigureAwait(false);

        // Terminal failure = Failed with the retry cleared (see EMAIL_OUTBOX_MESSAGE.RecordAttemptFailed).
        var failedCount = await dbContext.EmailOutboxMessages()
            .AsNoTracking()
            .Where(m => m.Status == EmailOutboxStatus.Failed && m.NextRetryAtUtc == null)
            .Take(IEmailOutboxHealthReader.CountCap)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        DateTime? oldestQueuedAtUtc = null;
        if (waitingCount > 0)
        {
            var oldestId = await waiting
                .OrderBy(m => m.Id)
                .Select(m => (Guid?)m.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (oldestId is { } id && UuidV7.TryGetTimestampUtc(id, out var queuedAtUtc))
            {
                oldestQueuedAtUtc = queuedAtUtc;
            }
        }

        return new EmailOutboxHealth(waitingCount, failedCount, oldestQueuedAtUtc);
    }
}
