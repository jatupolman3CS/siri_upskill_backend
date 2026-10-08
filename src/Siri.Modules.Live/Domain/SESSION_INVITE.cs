using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Domain;

/// <summary>
/// The record that one participant (a learner or the course's instructor) was told about one live session
/// — the idempotency ledger behind the invite/ICS/reminder e-mails (docs/contracts/
/// P11-04-live-invites-ics-reminders.md §2.2). Every e-mail the reconcile/reminder jobs send is tied to a
/// transition of this row in the same <c>SaveChanges</c> as the outbox row, so re-running a job never sends
/// twice. Rows are never deleted: they are also the evidence of who was invited or un-invited, so
/// withdrawing an invite is a status transition (<see cref="MarkCancelled"/>), not a removal.
/// <para>
/// UNIQUE (<see cref="SESSION_ID"/>, <see cref="USER_ID"/>): a participant has at most one invite per
/// session, whatever their role. No FK to <c>catalog.CourseLiveSessions</c> or <c>identity.Users</c> —
/// cross-module/cross-schema, same reasoning as <c>ENROLLMENT.USER_ID</c>.
/// </para>
/// <para>
/// <b>UPPERCASE naming (docs/DECISIONS.md D-17):</b> UPPERCASE properties ↔ UPPER_SNAKE_CASE columns,
/// except the four <see cref="IAuditable"/> properties which stay PascalCase (see
/// <c>Siri.Modules.Community.Domain.DISCUSSION</c>).
/// </para>
/// <para>
/// <see cref="ERROR"/> holds a short code only (e.g. <c>no_contact</c>) — never an e-mail address.
/// </para>
/// </summary>
public sealed class SESSION_INVITE : IAuditable
{
    private const int ErrorMaxLength = 300;

    /// <summary>EF Core materialization only.</summary>
    private SESSION_INVITE()
    {
    }

    public Guid SESSION_INVITE_ID { get; private set; }

    /// <summary><c>CATALOG.COURSE_LIVE_SESSIONS.Id</c> — no FK (cross-schema).</summary>
    public Guid SESSION_ID { get; private set; }

    /// <summary><c>identity.Users.Id</c> — no FK (cross-schema).</summary>
    public Guid USER_ID { get; private set; }

    public LiveParticipantRole ROLE { get; private set; }

    public InviteStatus STATUS { get; private set; }

    /// <summary>The ICS <c>SEQUENCE</c> of the last e-mail sent for this invite (a CANCEL counts too).
    /// Calendar clients ignore a SEQUENCE lower than one they have already applied, so this only ever
    /// grows — see <see cref="NextSequence"/>.</summary>
    public int? ICS_SEQUENCE_SENT { get; private set; }

    /// <summary>When the first invitation was sent; kept across re-invites.</summary>
    public DateTime? INVITE_SENT_AT_UTC { get; private set; }

    public DateTime? CANCEL_SENT_AT_UTC { get; private set; }

    public DateTime? REMINDER_24H_SENT_AT_UTC { get; private set; }

    public DateTime? REMINDER_1H_SENT_AT_UTC { get; private set; }

    /// <summary>When this participant was last added to the Google Calendar event's attendee list (opt-in
    /// per course, P11-04 §6); <c>null</c> = not on the Google event.</summary>
    public DateTime? GOOGLE_ATTENDEE_SYNCED_AT_UTC { get; private set; }

    /// <summary>Short error code only — never an e-mail address.</summary>
    public string? ERROR { get; private set; }

    /// <summary>EF concurrency token, rotated by <c>ConcurrencyTokenInterceptor</c>.</summary>
    public byte[] ROW_VERSION { get; private set; } = [];

    // ---- IAuditable (stays PascalCase — see this class's doc comment) ---------------------------
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    /// <summary>A participant who should be invited to <paramref name="sessionId"/> — starts
    /// <see cref="InviteStatus.Pending"/> with nothing sent.</summary>
    public static SESSION_INVITE Create(Guid sessionId, Guid userId, LiveParticipantRole role, IClock clock)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(clock);

        return new SESSION_INVITE
        {
            SESSION_INVITE_ID = UuidV7.NewId(),
            SESSION_ID = sessionId,
            USER_ID = userId,
            ROLE = role,
            STATUS = InviteStatus.Pending,
            CreatedAtUtc = clock.UtcNow,
        };
    }

    /// <summary>
    /// The SEQUENCE to put on the next ICS e-mail for this invite: at least the session's own
    /// <paramref name="meetingSequence"/> (<c>SESSION_MEETING.ICS_SEQUENCE</c>), but never lower than one past
    /// what was already sent — which keeps it monotonic even for CANCEL → re-invite.
    /// </summary>
    public int NextSequence(int meetingSequence) => Math.Max(meetingSequence, (ICS_SEQUENCE_SENT ?? -1) + 1);

    /// <summary>An invitation with ICS <paramref name="sequence"/> was sent (first invite, or an updated
    /// one after a reschedule). Allowed from <see cref="InviteStatus.Pending"/> and
    /// <see cref="InviteStatus.Invited"/>; the sequence may not go backwards.</summary>
    public void MarkInvited(int sequence, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureStatusIsOneOf(InviteStatus.Pending, InviteStatus.Invited);
        EnsureSequenceNotBackwards(sequence);

        STATUS = InviteStatus.Invited;
        ICS_SEQUENCE_SENT = sequence;
        INVITE_SENT_AT_UTC ??= clock.UtcNow;
        ERROR = null;
    }

    /// <summary>The invitation was withdrawn (CANCEL sent — or, for an invite that was never sent, dropped
    /// silently). Allowed from <see cref="InviteStatus.Pending"/> and <see cref="InviteStatus.Invited"/>.</summary>
    public void MarkCancelled(int sequence, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureStatusIsOneOf(InviteStatus.Pending, InviteStatus.Invited);
        EnsureSequenceNotBackwards(sequence);

        STATUS = InviteStatus.Cancelled;
        CANCEL_SENT_AT_UTC = clock.UtcNow;
        ICS_SEQUENCE_SENT = sequence;
    }

    /// <summary>The participant is eligible again (e.g. the enrollment was reactivated): back to
    /// <see cref="InviteStatus.Pending"/>, clearing the cancel/reminder/attendee stamps. The sent SEQUENCE
    /// and first-invite time are kept so the next invitation stays monotonic.</summary>
    public void Reinvite()
    {
        EnsureStatusIsOneOf(InviteStatus.Cancelled);

        STATUS = InviteStatus.Pending;
        CANCEL_SENT_AT_UTC = null;
        REMINDER_24H_SENT_AT_UTC = null;
        REMINDER_1H_SENT_AT_UTC = null;
        GOOGLE_ATTENDEE_SYNCED_AT_UTC = null;
        ERROR = null;
    }

    /// <summary>Nothing could be sent (e.g. <c>no_contact</c>). Not allowed once cancelled.</summary>
    public void MarkSkipped(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        EnsureStatusIsOneOf(InviteStatus.Pending, InviteStatus.Invited, InviteStatus.Skipped);

        STATUS = InviteStatus.Skipped;
        ERROR = code.Length <= ErrorMaxLength ? code : code[..ErrorMaxLength];
    }

    /// <summary>The 24-hour reminder was sent (first time only is recorded).</summary>
    public void MarkReminder24h(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureStatusIsOneOf(InviteStatus.Invited);

        REMINDER_24H_SENT_AT_UTC ??= clock.UtcNow;
    }

    /// <summary>The 1-hour reminder was sent (first time only is recorded).</summary>
    public void MarkReminder1h(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureStatusIsOneOf(InviteStatus.Invited);

        REMINDER_1H_SENT_AT_UTC ??= clock.UtcNow;
    }

    /// <summary>The session moved: both reminders become due again relative to the new time.</summary>
    public void ResetReminders()
    {
        REMINDER_24H_SENT_AT_UTC = null;
        REMINDER_1H_SENT_AT_UTC = null;
    }

    /// <summary>The participant is now on the Google Calendar event's attendee list.</summary>
    public void MarkAttendeeSynced(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        GOOGLE_ATTENDEE_SYNCED_AT_UTC = clock.UtcNow;
    }

    /// <summary>The participant was removed from (or never made it onto) the Google event's attendee list.</summary>
    public void ClearAttendeeSynced() => GOOGLE_ATTENDEE_SYNCED_AT_UTC = null;

    private void EnsureStatusIsOneOf(params InviteStatus[] allowed)
    {
        if (!allowed.Contains(STATUS))
        {
            throw new InvalidOperationException($"An invite in status {STATUS} does not allow this transition.");
        }
    }

    private void EnsureSequenceNotBackwards(int sequence)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "ICS SEQUENCE cannot be negative.");
        }

        if (ICS_SEQUENCE_SENT is { } sent && sequence < sent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence), sequence, $"ICS SEQUENCE must not go backwards (last sent: {sent}).");
        }
    }
}
