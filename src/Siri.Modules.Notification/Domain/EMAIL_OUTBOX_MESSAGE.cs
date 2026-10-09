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

    /// <summary>iCalendar <c>METHOD</c> values an outbox email may carry (RFC 5546) — also the exact
    /// strings stored in <see cref="CalendarMethod"/>.</summary>
    public const string CalendarMethodRequest = "REQUEST";

    public const string CalendarMethodCancel = "CANCEL";

    public const string CalendarMethodPublish = "PUBLISH";

    /// <summary>Upper bound on <see cref="CalendarIcs"/> (characters) — a batch invite of ≤50 VEVENTs is far
    /// below this; the cap just keeps a runaway builder from filling the table.</summary>
    public const int CalendarIcsMaxLength = 200_000;

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

    /// <summary>When the message was last handed to the message broker (<see cref="MarkQueued"/>); <c>null</c> while it has
    /// never been (always, under the database-polling transport). Together with <see cref="EmailOutboxStatus.Queued"/> it lets the
    /// relay notice a record that was published but never acted on, and publish it again.</summary>
    public DateTime? QueuedAtUtc { get; private set; }

    /// <summary><c>true</c> once every delivery attempt has failed and no retry is scheduled — the message is a dead letter that
    /// needs a human (the row, with <see cref="LastError"/>, is the dead-letter record).</summary>
    public bool IsExhausted => Status == EmailOutboxStatus.Failed && NextRetryAtUtc is null;

    /// <summary>The iCalendar (RFC 5545) document attached to this email as a <c>text/calendar</c> part, or
    /// <c>null</c> for an ordinary email (task P11-04, docs/contracts/P11-04-live-invites-ics-reminders.md
    /// §2.1). Always set together with <see cref="CalendarMethod"/>. The builder of the document is
    /// responsible for keeping meeting-room URLs out of it — this entity only stores what it is given.</summary>
    public string? CalendarIcs { get; private set; }

    /// <summary>The iCalendar <c>METHOD</c> of <see cref="CalendarIcs"/> — one of
    /// <see cref="CalendarMethodRequest"/>, <see cref="CalendarMethodCancel"/> or
    /// <see cref="CalendarMethodPublish"/>; <c>null</c> exactly when <see cref="CalendarIcs"/> is.</summary>
    public string? CalendarMethod { get; private set; }

    /// <summary>
    /// Queues a new email for delivery, in <see cref="EmailOutboxStatus.Pending"/> with zero
    /// attempts. <paramref name="toEmail"/> is stored exactly as given — address validation/
    /// normalization is the caller's concern; this entity only owns the outbox lifecycle.
    /// </summary>
    public static EMAIL_OUTBOX_MESSAGE Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey) =>
        Enqueue(toEmail, subject, bodyHtml, templateKey, calendarMethod: null, calendarIcs: null);

    /// <summary>
    /// Same as the four-argument overload, optionally attaching an iCalendar part. The two calendar
    /// arguments must be given together or not at all; when given, <paramref name="calendarMethod"/> must
    /// be exactly <c>REQUEST</c>, <c>CANCEL</c> or <c>PUBLISH</c> and <paramref name="calendarIcs"/> must be a
    /// non-empty iCalendar document (at most <see cref="CalendarIcsMaxLength"/> characters) that starts with
    /// <c>BEGIN:VCALENDAR</c>.
    /// </summary>
    public static EMAIL_OUTBOX_MESSAGE Enqueue(
        string toEmail, string subject, string bodyHtml, string? templateKey, string? calendarMethod, string? calendarIcs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyHtml);
        ValidateCalendarPart(calendarMethod, calendarIcs);

        return new EMAIL_OUTBOX_MESSAGE
        {
            Id = UuidV7.NewId(),
            ToEmail = toEmail,
            Subject = subject,
            BodyHtml = bodyHtml,
            TemplateKey = templateKey,
            CalendarMethod = calendarMethod,
            CalendarIcs = calendarIcs,
            Status = EmailOutboxStatus.Pending,
            Attempts = 0,
        };
    }

    private static void ValidateCalendarPart(string? calendarMethod, string? calendarIcs)
    {
        if (calendarMethod is null && calendarIcs is null)
        {
            return;
        }

        if (calendarMethod is null || calendarIcs is null)
        {
            throw new ArgumentException("A calendar method and its iCalendar content must be given together.");
        }

        if (calendarMethod is not (CalendarMethodRequest or CalendarMethodCancel or CalendarMethodPublish))
        {
            throw new ArgumentException(
                $"Calendar method must be {CalendarMethodRequest}, {CalendarMethodCancel} or {CalendarMethodPublish}.",
                nameof(calendarMethod));
        }

        if (string.IsNullOrWhiteSpace(calendarIcs))
        {
            throw new ArgumentException("Calendar content cannot be empty.", nameof(calendarIcs));
        }

        if (calendarIcs.Length > CalendarIcsMaxLength)
        {
            throw new ArgumentException($"Calendar content must be at most {CalendarIcsMaxLength} characters.", nameof(calendarIcs));
        }

        if (!calendarIcs.StartsWith("BEGIN:VCALENDAR", StringComparison.Ordinal))
        {
            throw new ArgumentException("Calendar content must start with BEGIN:VCALENDAR.", nameof(calendarIcs));
        }
    }

    /// <summary>
    /// Records that the message was handed to the broker and now waits for a consumer. Allowed from
    /// <see cref="EmailOutboxStatus.Pending"/>, from <see cref="EmailOutboxStatus.Failed"/> (a retry that has come due), and from
    /// <see cref="EmailOutboxStatus.Queued"/> itself (re-publishing a record that went stale). Clears
    /// <see cref="NextRetryAtUtc"/>: from here on the consumer decides the next step. <see cref="Attempts"/> is untouched, so the
    /// retry budget is shared across publishes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Already sent, or every attempt is used up — there is nothing left to deliver.</exception>
    public void MarkQueued(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status == EmailOutboxStatus.Sent)
        {
            throw new InvalidOperationException("Cannot queue an already-sent message.");
        }

        if (IsExhausted)
        {
            throw new InvalidOperationException("Cannot queue a message that already exhausted every attempt.");
        }

        Status = EmailOutboxStatus.Queued;
        QueuedAtUtc = clock.UtcNow;
        NextRetryAtUtc = null;
    }

    /// <summary>
    /// Undoes a <see cref="MarkQueued"/> whose record the broker never accepted, putting the row back exactly as it was so the relay picks
    /// it up again on its next cycle (a <see cref="EmailOutboxStatus.Failed"/> row keeps its retry time, a <see cref="EmailOutboxStatus.Pending"/>
    /// one stays pending). Compare-and-swap: it only acts while the row is still <see cref="EmailOutboxStatus.Queued"/> with exactly the
    /// <paramref name="queuedAtUtc"/> stamp of that claim — if a consumer (or another claim) has moved the row on since, it does nothing and returns
    /// <c>false</c>, never overwriting a newer outcome.
    /// </summary>
    /// <returns><c>true</c> when the row was restored.</returns>
    public bool RevertQueued(
        EmailOutboxStatus previousStatus,
        DateTime? previousNextRetryAtUtc,
        DateTime? previousQueuedAtUtc,
        DateTime queuedAtUtc)
    {
        if (previousStatus is not (EmailOutboxStatus.Pending or EmailOutboxStatus.Failed or EmailOutboxStatus.Queued))
        {
            throw new ArgumentOutOfRangeException(nameof(previousStatus), previousStatus, "A claimed row was Pending, Failed or Queued before it was claimed.");
        }

        if (Status != EmailOutboxStatus.Queued || QueuedAtUtc != queuedAtUtc)
        {
            return false;
        }

        Status = previousStatus;
        NextRetryAtUtc = previousNextRetryAtUtc;
        QueuedAtUtc = previousQueuedAtUtc;
        return true;
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
