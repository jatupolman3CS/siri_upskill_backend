using Siri.SharedKernel;

namespace Siri.Modules.Notification.Domain;

/// <summary>
/// A single outbound email queued for delivery — the generic mechanism P0-19 builds so that later,
/// specific use cases (P0-15's "confirm your email", P0-21's "reset your password", ...) can just
/// call <see cref="Enqueue"/> instead of talking to an SMTP server inline from a request handler.
/// Actually sending happens out-of-band, from <c>Infrastructure/EmailOutboxSenderJob.cs</c>'s
/// recurring Hangfire job, which drives this entity's state machine through
/// <see cref="RecordSent"/>/<see cref="RecordAttemptFailed"/> — never by mutating EF-tracked
/// properties directly from outside (backend.md: "เปลี่ยนสถานะผ่าน method").
/// <para>
/// <b>Retry/backoff policy</b> (deliberately bounded — not "retry forever"):
/// <list type="bullet">
/// <item>Up to <see cref="MaxAttempts"/> (5) delivery attempts total.</item>
/// <item>After each failed attempt, while attempts remain, <see cref="NextRetryAtUtc"/> is set to an
/// exponentially growing delay: 1, 2, 4, 8 minutes after attempts 1–4 respectively
/// (<c>2^(attempt-1)</c> minutes). A transient SMTP hiccup gets retried almost immediately; a
/// sustained outage backs off instead of hammering the mail server.</item>
/// <item>Once the 5th attempt also fails, the message becomes terminal: <see cref="Status"/> stays
/// <see cref="EmailOutboxStatus.Failed"/> but <see cref="NextRetryAtUtc"/> is cleared to
/// <c>null</c>, so the sender job's due-message query
/// (<c>Status == Pending OR (Status == Failed AND NextRetryAtUtc &lt;= now)</c>) stops matching this
/// row. <see cref="LastError"/> keeps the most recent failure reason for diagnostics.</item>
/// </list>
/// There is deliberately no separate "Sending" status: the sender job processes each due message and
/// records the outcome (<see cref="RecordSent"/> or <see cref="RecordAttemptFailed"/>) in the same
/// pass, so there is no observable window where an in-flight/"Sending" state would carry information
/// the query above doesn't already have. If a future job design splits dispatch from confirmation
/// across steps, add that status then — not speculatively now.
/// </para>
/// </summary>
public sealed class EMAIL_OUTBOX_MESSAGE
{
    /// <summary>Total attempts allowed (including the first) before giving up permanently.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Bound on <see cref="LastError"/> — see <c>EmailOutboxMessageConfiguration</c>'s
    /// matching <c>HasMaxLength</c>. Exception messages get truncated to this, never the full
    /// stack trace (security.md: error text stored here is for internal diagnostics only).</summary>
    private const int LastErrorMaxLength = 2000;

    /// <summary>EF Core materialization only — never used to build a usable instance from code.</summary>
    private EMAIL_OUTBOX_MESSAGE()
    {
    }

    /// <summary>UUIDv7 — besides the usual index-locality reason (database.md), this also gives the
    /// sender job a free, correct-enough FIFO ordering ("oldest queued first") via <c>ORDER BY Id</c>
    /// without needing a separate <c>CreatedAtUtc</c> column beyond what docs/DATABASE.md's
    /// <c>notify.EmailOutbox</c> definition lists.</summary>
    public Guid Id { get; private set; }

    public string ToEmail { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string BodyHtml { get; private set; } = string.Empty;

    /// <summary>Identifies which template rendered <see cref="BodyHtml"/> (e.g.
    /// <c>GenericNotificationEmailTemplate.Key</c>) for observability/debugging. Optional — nothing
    /// requires every queued email to have come from a named template.</summary>
    public string? TemplateKey { get; private set; }

    public EmailOutboxStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime? NextRetryAtUtc { get; private set; }

    public DateTime? SentAtUtc { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>
    /// Queues a new email for delivery, in <see cref="EmailOutboxStatus.Pending"/> with zero
    /// attempts. <paramref name="toEmail"/> is stored exactly as given — address validation/
    /// normalization is the caller's concern; this entity only owns the outbox lifecycle.
    /// </summary>
    public static EMAIL_OUTBOX_MESSAGE Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyHtml);

        return new EMAIL_OUTBOX_MESSAGE
        {
            Id = UuidV7.NewId(),
            ToEmail = toEmail,
            Subject = subject,
            BodyHtml = bodyHtml,
            TemplateKey = templateKey,
            Status = EmailOutboxStatus.Pending,
            Attempts = 0,
        };
    }

    /// <summary>Records a successful delivery. Idempotent if already <see cref="EmailOutboxStatus.Sent"/>.</summary>
    public void RecordSent(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status == EmailOutboxStatus.Sent)
        {
            return; // idempotent — already recorded
        }

        if (Status == EmailOutboxStatus.Failed && NextRetryAtUtc is null)
        {
            throw new InvalidOperationException(
                "Cannot record a send for a message that already exhausted every retry attempt.");
        }

        Status = EmailOutboxStatus.Sent;
        SentAtUtc = clock.UtcNow;
        NextRetryAtUtc = null;
    }

    /// <summary>
    /// Records a failed delivery attempt and applies the backoff policy documented on this type —
    /// moves to a terminal <see cref="EmailOutboxStatus.Failed"/> (no further retry) once
    /// <see cref="MaxAttempts"/> is reached.
    /// </summary>
    public void RecordAttemptFailed(string error, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ArgumentNullException.ThrowIfNull(clock);

        if (Status == EmailOutboxStatus.Sent)
        {
            throw new InvalidOperationException("Cannot record a failed attempt for an already-sent message.");
        }

        Attempts++;
        LastError = Truncate(error, LastErrorMaxLength);
        Status = EmailOutboxStatus.Failed;

        NextRetryAtUtc = Attempts >= MaxAttempts
            ? null // exhausted every attempt — terminal; the sender job's query stops matching this row
            : clock.UtcNow.Add(BackoffDelay(Attempts));
    }

    /// <summary>Exponential backoff: <c>2^(attempt-1)</c> minutes — 1, 2, 4, 8 minutes after
    /// attempts 1–4 (attempt 5 never schedules a retry — see <see cref="RecordAttemptFailed"/>).</summary>
    private static TimeSpan BackoffDelay(int attempt) => TimeSpan.FromMinutes(Math.Pow(2, attempt - 1));

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
