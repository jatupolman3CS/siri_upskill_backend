namespace Siri.Modules.Notification.Domain;

/// <summary>
/// Delivery state of an <see cref="EMAIL_OUTBOX_MESSAGE"/>. Stored as a string in the database (see
/// <c>Infrastructure/EmailOutboxMessageConfiguration.cs</c>), following the same
/// <c>HasConversion&lt;string&gt;()</c> convention <c>Siri.Modules.Identity.Domain.UserStatus</c>
/// established as the first enum in the codebase.
/// </summary>
public enum EmailOutboxStatus
{
    /// <summary>Queued, no delivery attempt made yet.</summary>
    Pending,

    /// <summary>Delivered successfully. Terminal — no further attempts.</summary>
    Sent,

    /// <summary>
    /// At least one delivery attempt failed. This single status covers two different situations,
    /// told apart by <see cref="EMAIL_OUTBOX_MESSAGE.NextRetryAtUtc"/> rather than by a 4th status
    /// name: non-null means "failed, will retry at that time"; null means "failed, exhausted every
    /// retry attempt, gave up for good". See <see cref="EMAIL_OUTBOX_MESSAGE"/>'s own doc comment for
    /// the full retry/backoff policy.
    /// </summary>
    Failed,
}
