using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Domain;

/// <summary>
/// The online room (Google Meet / Zoom / Teams link) behind one live session
/// (<c>CATALOG.COURSE_LIVE_SESSIONS</c> row) — docs/contracts/P11-03-live-module-google-meetings.md §2.2/§2.3.
/// One session = at most one meeting (<see cref="SESSION_ID"/> is UNIQUE). Rows are staged by
/// <c>LiveMeetingSink</c> inside the Catalog handler's own transaction (before the session row exists in
/// the database — hence the missing FK) and driven to a usable room by the <c>live-meeting-sync</c> job
/// through the state machine below. Nothing outside this class changes <see cref="SYNC_STATUS"/>.
/// <para>
/// <b>The room URL is a capability, never plaintext:</b> <see cref="MEET_URL_ENCRYPTED"/> holds
/// <c>ISensitiveDataProtector.Encrypt(url)</c>; this entity never sees the plaintext (callers encrypt
/// before calling <see cref="SetManualLink"/>/<see cref="RecordGoogleSynced"/>) and the only reader is
/// <c>SessionMeetingService.RevealUrl</c>. <see cref="ERROR"/> may only hold a short error code — never a
/// token, URL or e-mail address.
/// </para>
/// <para>
/// <b>UPPERCASE naming (docs/DECISIONS.md D-17):</b> UPPERCASE properties ↔ UPPER_SNAKE_CASE columns,
/// except the four <see cref="IAuditable"/> properties which stay PascalCase (see
/// <c>Siri.Modules.Community.Domain.DISCUSSION</c>).
/// </para>
/// <para>
/// <b>State machine</b> (<see cref="MeetingSyncStatus"/>): <c>Pending</c> → job creates/patches the Google
/// event → <c>Synced</c>; no Google account and no URL → <c>AwaitingLink</c>; Google token unusable →
/// <c>NeedsReconnect</c>; five failed attempts → <c>Failed</c>; cancelled session / manual link replacing a
/// Google event → <c>PendingDelete</c> → <c>Deleted</c> (or <c>Synced</c> when a manual URL survives).
/// </para>
/// </summary>
public sealed class SESSION_MEETING : IAuditable
{
    /// <summary>Attempts allowed before <see cref="MeetingSyncStatus.Failed"/>.</summary>
    public const int MaxAttempts = 5;

    private const int ProviderEventIdMaxLength = 200;
    private const int ErrorMaxLength = 500;

    /// <summary>Delay before the retry that follows failed attempt 1, 2, 3 and 4 respectively.</summary>
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60),
    ];

    /// <summary>EF Core materialization only.</summary>
    private SESSION_MEETING()
    {
    }

    public Guid SESSION_MEETING_ID { get; private set; }

    /// <summary><c>CATALOG.COURSE_LIVE_SESSIONS.Id</c> — no FK (cross-schema, and the session row is not yet
    /// in the database when <c>LiveMeetingSink</c> stages this one). UNIQUE.</summary>
    public Guid SESSION_ID { get; private set; }

    /// <summary>Denormalized by the sync job from <c>LiveSessionContext</c> on first processing — used to
    /// find this instructor's rows to reset when they reconnect Google. <c>null</c> until then.</summary>
    public Guid? INSTRUCTOR_USER_ID { get; private set; }

    /// <summary><c>null</c> = not decided yet (a freshly staged row, or after <see cref="RequestResync"/>).</summary>
    public MeetingProvider? PROVIDER { get; private set; }

    /// <summary>FK → <c>LIVE.INSTRUCTOR_GOOGLE_ACCOUNTS</c> (same module, <c>NoAction</c>); set while the
    /// provider is <see cref="MeetingProvider.GoogleMeet"/>.</summary>
    public Guid? INSTRUCTOR_GOOGLE_ACCOUNT_ID { get; private set; }

    /// <summary>Google Calendar event id.</summary>
    public string? PROVIDER_EVENT_ID { get; private set; }

    /// <summary><c>ISensitiveDataProtector.Encrypt(url)</c> — read only through <c>SessionMeetingService.RevealUrl</c>.</summary>
    public string? MEET_URL_ENCRYPTED { get; private set; }

    public MeetingSyncStatus SYNC_STATUS { get; private set; }

    /// <summary>Bumped when the session's time/title changes or it is cancelled — the ICS <c>SEQUENCE</c>
    /// floor that P11-04's invite e-mails use.</summary>
    public int ICS_SEQUENCE { get; private set; }

    public int ATTEMPTS { get; private set; }

    public DateTime? NEXT_RETRY_AT_UTC { get; private set; }

    public DateTime? LAST_SYNC_AT_UTC { get; private set; }

    /// <summary>Short error code only (e.g. <c>conference_pending</c>, <c>orphan_event</c>).</summary>
    public string? ERROR { get; private set; }

    /// <summary>Set once when the instructor has been alerted about this meeting (prevents repeats).</summary>
    public DateTime? MEETING_ALERT_SENT_AT_UTC { get; private set; }

    /// <summary>Set once when the instructor was told, within 24 hours of the start, that this session still has no usable
    /// room (P11-04's reminder job). A separate stamp from <see cref="MEETING_ALERT_SENT_AT_UTC"/> on purpose: that one is
    /// consumed by the "paste a link" alert the moment the meeting becomes <c>AwaitingLink</c> — typically days earlier — and
    /// sharing it would mean the closer-to-the-class warning could never fire.</summary>
    public DateTime? READINESS_ALERT_SENT_AT_UTC { get; private set; }

    /// <summary>Set once when the instructor was told that this session has more invited learners than
    /// <c>Live:GoogleAttendeeCap</c>, so the Google attendee sync is switched off for it (P11-04 §6). Its own stamp — the
    /// over-the-cap condition persists run after run and must not re-alert; cleared again when the count falls back under the cap
    /// so a later crossing alerts afresh.</summary>
    public DateTime? ATTENDEE_SYNC_ALERT_SENT_AT_UTC { get; private set; }

    /// <summary>EF concurrency token, rotated by <c>ConcurrencyTokenInterceptor</c>.</summary>
    public byte[] ROW_VERSION { get; private set; } = [];

    /// <summary>
    /// Whether learners/the instructor can be given a room right now: a URL exists and the meeting has not
    /// been deleted. <c>Pending</c> (a Google patch after a reschedule), <c>NeedsReconnect</c> and
    /// <c>Failed</c> rows that still hold their old URL stay usable. Used by both the publish gate and the
    /// join gate. Derived — not mapped (no setter).
    /// </summary>
    public bool IsUsable => MEET_URL_ENCRYPTED != null && SYNC_STATUS != MeetingSyncStatus.Deleted;

    /// <summary>Whether <see cref="RequestResync"/> would succeed — <c>Failed</c> in any case, and
    /// <c>NeedsReconnect</c>/<c>AwaitingLink</c> only while no URL exists (a held URL means nothing is
    /// blocked). Derived — not mapped.</summary>
    public bool CanRequestResync => SYNC_STATUS switch
    {
        MeetingSyncStatus.Failed => true,
        MeetingSyncStatus.NeedsReconnect or MeetingSyncStatus.AwaitingLink => MEET_URL_ENCRYPTED is null,
        _ => false,
    };

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

    /// <summary>Stages a meeting for a freshly scheduled session: <see cref="MeetingSyncStatus.Pending"/>,
    /// provider not decided yet. Takes no clock — <c>LiveMeetingSink</c> must stage without querying
    /// anything; <c>CreatedAtUtc</c> comes from the auditing interceptor.</summary>
    public static SESSION_MEETING Stage(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        return new SESSION_MEETING
        {
            SESSION_MEETING_ID = UuidV7.NewId(),
            SESSION_ID = sessionId,
            SYNC_STATUS = MeetingSyncStatus.Pending,
            PROVIDER = null,
            ICS_SEQUENCE = 0,
            ATTEMPTS = 0,
        };
    }

    // ---- Session lifecycle (called by LiveMeetingSink) -----------------------------------------

    /// <summary>The session's time/title changed: bumps <see cref="ICS_SEQUENCE"/>, and if a Google event
    /// exists asks the job to patch it (<see cref="MeetingSyncStatus.Pending"/>, retry state reset). A
    /// manual/other-provider meeting keeps its status. A meeting already on its way out
    /// (<c>PendingDelete</c>/<c>Deleted</c>) is never resurrected by an edit — only the sequence moves.</summary>
    public void MarkSessionChanged()
    {
        ICS_SEQUENCE++;

        if (SYNC_STATUS is MeetingSyncStatus.PendingDelete or MeetingSyncStatus.Deleted)
        {
            return;
        }

        if (PROVIDER == MeetingProvider.GoogleMeet && PROVIDER_EVENT_ID is not null)
        {
            SYNC_STATUS = MeetingSyncStatus.Pending;
            ATTEMPTS = 0;
            NEXT_RETRY_AT_UTC = null;
        }
    }

    /// <summary>The session was cancelled: bumps <see cref="ICS_SEQUENCE"/> (so the CANCEL e-mail outranks
    /// earlier invites) and moves to <c>PendingDelete</c> if a Google event must be removed, else
    /// <c>Deleted</c>. Idempotent — cancelling twice does not bump the sequence twice.</summary>
    public void MarkSessionCancelled()
    {
        if (SYNC_STATUS is MeetingSyncStatus.PendingDelete or MeetingSyncStatus.Deleted)
        {
            return;
        }

        ICS_SEQUENCE++;
        ATTEMPTS = 0;
        NEXT_RETRY_AT_UTC = null;
        SYNC_STATUS = PROVIDER_EVENT_ID is null ? MeetingSyncStatus.Deleted : MeetingSyncStatus.PendingDelete;
    }

    // ---- Instructor actions ---------------------------------------------------------------------

    /// <summary>
    /// The instructor pasted their own room link (already validated by <c>MeetingLinkValidator</c> and
    /// encrypted by the caller). Switches the provider to <see cref="MeetingProvider.Manual"/>; if a Google
    /// event still exists it goes to <c>PendingDelete</c> so the job removes it, otherwise
    /// <c>Synced</c>. A deleted (cancelled) meeting cannot be given a link.
    /// </summary>
    public void SetManualLink(string meetUrlEncrypted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meetUrlEncrypted);

        if (SYNC_STATUS == MeetingSyncStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot set a meeting link on a deleted meeting.");
        }

        PROVIDER = MeetingProvider.Manual;
        MEET_URL_ENCRYPTED = meetUrlEncrypted;
        ERROR = null;
        ATTEMPTS = 0;
        NEXT_RETRY_AT_UTC = null;
        SYNC_STATUS = PROVIDER_EVENT_ID is null ? MeetingSyncStatus.Synced : MeetingSyncStatus.PendingDelete;
    }

    /// <summary>The instructor asked to retry (typically after reconnecting Google). Back to
    /// <c>Pending</c> with the provider undecided and retry state reset. Throws
    /// <see cref="InvalidOperationException"/> unless <see cref="CanRequestResync"/> — the service maps
    /// that to 409 <c>live.meeting_not_resyncable</c>.</summary>
    public void RequestResync()
    {
        if (!CanRequestResync)
        {
            throw new InvalidOperationException($"A meeting in status {SYNC_STATUS} cannot be resynced.");
        }

        SYNC_STATUS = MeetingSyncStatus.Pending;
        PROVIDER = null;
        ATTEMPTS = 0;
        NEXT_RETRY_AT_UTC = null;
    }

    // ---- Sync job outcomes ----------------------------------------------------------------------

    /// <summary>Denormalizes the session's instructor on first processing (job-only, idempotent).</summary>
    public void AssignInstructor(Guid instructorUserId)
    {
        if (instructorUserId == Guid.Empty)
        {
            throw new ArgumentException("Instructor user ID cannot be empty.", nameof(instructorUserId));
        }

        INSTRUCTOR_USER_ID = instructorUserId;
    }

    /// <summary>Records the provider decision <em>before</em> the first Google call, so
    /// <c>PROVIDER != null</c> from the first pass whether that call succeeds or not (P11-04 waits for it
    /// before e-mailing the instructor). <paramref name="accountId"/> is required — and stored — only for
    /// <see cref="MeetingProvider.GoogleMeet"/>.</summary>
    public void AssignProvider(MeetingProvider provider, Guid? accountId)
    {
        if (provider == MeetingProvider.GoogleMeet)
        {
            if (accountId is null || accountId == Guid.Empty)
            {
                throw new ArgumentException("A Google account is required for the GoogleMeet provider.", nameof(accountId));
            }

            INSTRUCTOR_GOOGLE_ACCOUNT_ID = accountId;
        }

        PROVIDER = provider;
    }

    /// <summary>The instructor has no connected Google account and no URL: waits for a manual link.</summary>
    public void ResolveAsAwaitingLink()
    {
        PROVIDER = MeetingProvider.Manual;
        SYNC_STATUS = MeetingSyncStatus.AwaitingLink;
        ATTEMPTS = 0;
        NEXT_RETRY_AT_UTC = null;
    }

    /// <summary>The Google event exists and the room URL is known (also used when adopting an event found by
    /// its private session id). <paramref name="provider"/> is <c>GoogleMeet</c> or <c>Logging</c>.</summary>
    public void RecordGoogleSynced(MeetingProvider provider, string eventId, string meetUrlEncrypted, Guid? accountId, IClock clock)
    {
        if (provider == MeetingProvider.Manual)
        {
            throw new ArgumentException("A Google/Logging sync cannot record the Manual provider.", nameof(provider));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(meetUrlEncrypted);
        ArgumentNullException.ThrowIfNull(clock);

        if (eventId.Length > ProviderEventIdMaxLength)
        {
            throw new ArgumentException($"Event id must be at most {ProviderEventIdMaxLength} characters.", nameof(eventId));
        }

        PROVIDER = provider;
        INSTRUCTOR_GOOGLE_ACCOUNT_ID = provider == MeetingProvider.GoogleMeet ? accountId : null;
        PROVIDER_EVENT_ID = eventId;
        MEET_URL_ENCRYPTED = meetUrlEncrypted;
        SYNC_STATUS = MeetingSyncStatus.Synced;
        ATTEMPTS = 0;
        NEXT_RETRY_AT_UTC = null;
        LAST_SYNC_AT_UTC = clock.UtcNow;
        ERROR = null;
    }

    /// <summary>The instructor's Google token is unusable. A URL that already exists stays valid.</summary>
    public void RecordNeedsReconnect(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        SYNC_STATUS = MeetingSyncStatus.NeedsReconnect;
        ERROR = Truncate(code, ErrorMaxLength);
        NEXT_RETRY_AT_UTC = null;
    }

    /// <summary>
    /// A transient or retry-worthy failure. Increments <see cref="ATTEMPTS"/>; after attempt 1/2/3/4 the
    /// next try is due 1/5/15/60 minutes later (status unchanged — <c>Pending</c> or <c>PendingDelete</c>);
    /// attempt 5 ends in <see cref="MeetingSyncStatus.Failed"/> with no further retry.
    /// </summary>
    public void RecordAttemptFailed(string code, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(clock);

        if (SYNC_STATUS is not (MeetingSyncStatus.Pending or MeetingSyncStatus.PendingDelete))
        {
            throw new InvalidOperationException($"Only a Pending/PendingDelete meeting can record a failed attempt (was {SYNC_STATUS}).");
        }

        ATTEMPTS++;
        ERROR = Truncate(code, ErrorMaxLength);

        if (ATTEMPTS >= MaxAttempts)
        {
            SYNC_STATUS = MeetingSyncStatus.Failed;
            NEXT_RETRY_AT_UTC = null;
            return;
        }

        NEXT_RETRY_AT_UTC = clock.UtcNow.Add(RetryBackoff[ATTEMPTS - 1]);
    }

    /// <summary>Forgets a Google event that no longer exists (404/410) so the job can create a new one.</summary>
    public void ClearProviderEvent()
    {
        PROVIDER_EVENT_ID = null;
    }

    /// <summary>Stores a short diagnostic code without changing state (e.g. <c>orphan_event</c> when a Google
    /// event could not be deleted because no account is available).</summary>
    public void RecordError(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        ERROR = Truncate(code, ErrorMaxLength);
    }

    /// <summary>A Google event deletion was handled (or deliberately skipped): forgets the event id and
    /// settles into <c>Synced</c> when a manual URL survives for a still-scheduled session, else
    /// <c>Deleted</c>.</summary>
    public void FinishDelete(bool sessionStillScheduled)
    {
        PROVIDER_EVENT_ID = null;
        ATTEMPTS = 0;
        NEXT_RETRY_AT_UTC = null;
        SYNC_STATUS = PROVIDER == MeetingProvider.Manual && MEET_URL_ENCRYPTED is not null && sessionStillScheduled
            ? MeetingSyncStatus.Synced
            : MeetingSyncStatus.Deleted;
    }

    /// <summary>The session finished before any room was created: nothing will be created for it. A URL that
    /// already exists leaves the meeting <c>Synced</c>, otherwise <c>Deleted</c>.</summary>
    public void ResolveEndedSession()
    {
        ATTEMPTS = 0;
        NEXT_RETRY_AT_UTC = null;
        SYNC_STATUS = MEET_URL_ENCRYPTED is not null ? MeetingSyncStatus.Synced : MeetingSyncStatus.Deleted;
    }

    /// <summary>The meeting already holds a usable room URL and there is nothing left for the sync job to do —
    /// e.g. a manual link that outlived the instructor's Google account. Settles into <c>Synced</c> and clears
    /// the retry state/error. Throws if there is no URL (that case is <see cref="ResolveAsAwaitingLink"/>).</summary>
    public void SettleWithExistingLink()
    {
        if (MEET_URL_ENCRYPTED is null)
        {
            throw new InvalidOperationException("A meeting without a room URL cannot be settled as Synced.");
        }

        ATTEMPTS = 0;
        NEXT_RETRY_AT_UTC = null;
        ERROR = null;
        SYNC_STATUS = MeetingSyncStatus.Synced;
    }

    /// <summary>The session no longer exists (or cannot be found): nothing left to do.</summary>
    public void MarkDeleted()
    {
        ATTEMPTS = 0;
        NEXT_RETRY_AT_UTC = null;
        SYNC_STATUS = MeetingSyncStatus.Deleted;
    }

    /// <summary>Stamps that the instructor has been alerted about this meeting (once).</summary>
    public void MarkAlertSent(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        MEETING_ALERT_SENT_AT_UTC ??= clock.UtcNow;
    }

    /// <summary>Stamps that the instructor has been warned (once) that the room is still not ready shortly before the class.</summary>
    public void MarkReadinessAlertSent(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        READINESS_ALERT_SENT_AT_UTC ??= clock.UtcNow;
    }

    /// <summary>Stamps that the instructor has been told (once) that the attendee sync is off for this session because it is over the cap.</summary>
    public void MarkAttendeeSyncAlertSent(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        ATTENDEE_SYNC_ALERT_SENT_AT_UTC ??= clock.UtcNow;
    }

    /// <summary>The session is back under the attendee cap: forgets the alert so a later crossing alerts again.</summary>
    public void ClearAttendeeSyncAlert() => ATTENDEE_SYNC_ALERT_SENT_AT_UTC = null;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
