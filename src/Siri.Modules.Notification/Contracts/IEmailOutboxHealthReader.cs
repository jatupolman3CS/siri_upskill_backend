namespace Siri.Modules.Notification.Contracts;

/// <summary>
/// A read-only summary of the e-mail outbox for operators — how many messages are still waiting to be delivered, how many were given up on, and
/// when the oldest waiting one was queued. Counts only: no recipient, subject, body or error text ever leaves the module.
/// </summary>
/// <param name="WaitingCount">Messages not delivered yet and still going to be attempted: queued (<c>Pending</c>) plus failed-but-scheduled-for-retry.
/// Capped at <see cref="IEmailOutboxHealthReader.CountCap"/>.</param>
/// <param name="FailedCount">Messages that exhausted every retry and will never be sent (terminal failures). Capped at <see cref="IEmailOutboxHealthReader.CountCap"/>.</param>
/// <param name="OldestWaitingQueuedAtUtc">When the oldest still-waiting message was queued, or <c>null</c> when nothing is waiting (or its
/// queue time cannot be determined).</param>
public sealed record EmailOutboxHealth(int WaitingCount, int FailedCount, DateTime? OldestWaitingQueuedAtUtc);

/// <summary>
/// The Notification module's public surface for observing the outbox's health (admin diagnostics) — the only way another module or host may look at it.
/// Bounded, read-only, no tracking; safe to call on every diagnostics request.
/// </summary>
public interface IEmailOutboxHealthReader
{
    /// <summary>Counts are capped at this value so the queries stay cheap however large the table grows.</summary>
    const int CountCap = 10_000;

    Task<EmailOutboxHealth> GetHealthAsync(CancellationToken cancellationToken);
}
