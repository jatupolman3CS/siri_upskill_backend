using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Domain;

/// <summary>
/// The automatic import of one live session's Google Meet recording into the platform (docs/contracts/P11-13-live-recording-auto-import.md §3, §6).
/// <b>One row per session at most</b> (<see cref="SESSION_ID"/> is UNIQUE); a row is only ever written for work to do (a Google-Workspace instructor with
/// recording access whose class ended), never for sessions the manual upload path covers. The <c>live-recording-import</c> job drives it through
/// <see cref="RecordingImportStatus"/>; nothing outside this class changes <see cref="STATUS"/>.
/// <para>
/// <b>State machine:</b> <c>Waiting</c> (searching Google, re-polled with a backoff) → <c>Transferring</c> (Drive → video provider, under a lease) →
/// <c>Processing</c> (provider transcodes) → <c>Attached</c>. Side exits: <c>NoRecording</c> (search window over), <c>Failed</c>, <c>NeedsReconnect</c>,
/// <c>Skipped</c>. A transient failure keeps the row alive with <see cref="ATTEMPTS"/>+1 and a backoff and ends in <c>Failed</c> at the configured maximum.
/// <c>Failed</c>/<c>NoRecording</c>/<c>NeedsReconnect</c> can be reset to <c>Waiting</c> by <see cref="ResetForRetry"/>.
/// </para>
/// <para>
/// <b>Never stored:</b> <see cref="ERROR_CODE"/> is a stable short code — never a message, URL, token, e-mail or file name. The Google ids are internal
/// references (<see cref="GOOGLE_RECORDING_NAME"/>, <see cref="GOOGLE_FILE_ID"/>); nothing here is returned by an API.
/// </para>
/// <para>
/// <b>UPPERCASE naming (docs/DECISIONS.md D-17):</b> UPPERCASE properties ↔ UPPER_SNAKE_CASE columns, except the four <see cref="IAuditable"/>
/// properties which stay PascalCase — <c>AuditableEntityInterceptor</c> looks them up by C# name (see <c>Siri.Modules.Community.Domain.DISCUSSION</c>).
/// No FK to <c>CATALOG.COURSE_LIVE_SESSIONS</c>/<c>identity.Users</c> (cross-schema), same reasoning as <see cref="SESSION_MEETING"/>.
/// </para>
/// </summary>
public sealed class SESSION_RECORDING_IMPORT : IAuditable
{
    public const int ErrorCodeMaxLength = 60;
    public const int GoogleIdMaxLength = 200;

    /// <summary>EF Core materialization only.</summary>
    private SESSION_RECORDING_IMPORT()
    {
    }

    public Guid SESSION_RECORDING_IMPORT_ID { get; private set; }

    /// <summary><c>CATALOG.COURSE_LIVE_SESSIONS.Id</c> — no FK (cross-schema). UNIQUE: at most one import per session.</summary>
    public Guid SESSION_ID { get; private set; }

    public Guid COURSE_ID { get; private set; }

    /// <summary><c>identity.Users.Id</c> of the session's instructor: whose Google token is used, and the owner of the media asset that is created.</summary>
    public Guid INSTRUCTOR_USER_ID { get; private set; }

    public RecordingImportStatus STATUS { get; private set; }

    /// <summary>Consecutive transient failures since the last success (network, 5xx, 429, a lease that expired) — reset by a successful step and by a retry.</summary>
    public int ATTEMPTS { get; private set; }

    /// <summary>When the job may touch the row next (<c>Waiting</c>/<c>Processing</c>). <c>null</c> for <c>Transferring</c> (the lease decides) and terminal states.</summary>
    public DateTime? NEXT_ATTEMPT_AT_UTC { get; private set; }

    /// <summary>While <c>Transferring</c>: the lease — a run that has not finished by then is presumed dead and the transfer is reclaimed.
    /// While <c>Processing</c>: the deadline for the video provider to finish transcoding (then <c>Failed(transcode_timeout)</c>). Otherwise <c>null</c>.</summary>
    public DateTime? LEASE_UNTIL_UTC { get; private set; }

    /// <summary>Meet resource name of the chosen recording (<c>conferenceRecords/…/recordings/…</c>) — an id, not a secret. Never logged.</summary>
    public string? GOOGLE_RECORDING_NAME { get; private set; }

    /// <summary>Drive file id of the recording. Never logged, never returned by an API.</summary>
    public string? GOOGLE_FILE_ID { get; private set; }

    /// <summary>The media asset created for the copy, once the transfer succeeded.</summary>
    public Guid? MEDIA_ASSET_ID { get; private set; }

    /// <summary>The lesson that now holds the recording (<c>Attached</c>).</summary>
    public Guid? EPISODE_ID { get; private set; }

    /// <summary>Stable code of the last failure / terminal reason (e.g. <c>file_too_large</c>, <c>recording_scope_missing</c>). Shown to the instructor as a code only.</summary>
    public string? ERROR_CODE { get; private set; }

    /// <summary>Session end + <c>SearchWindowHours</c>: the search gives up after this and ends as <c>NoRecording</c>.</summary>
    public DateTime SEARCH_UNTIL_UTC { get; private set; }

    /// <summary>Set when the row reaches a terminal state; cleared by a retry.</summary>
    public DateTime? COMPLETED_AT_UTC { get; private set; }

    /// <summary>EF concurrency token, rotated by <c>ConcurrencyTokenInterceptor</c>. The job "claims" a row by saving with the version it read.</summary>
    public byte[] ROW_VERSION { get; private set; } = [];

    /// <summary>Terminal: nothing more will happen unless the instructor retries. Derived — not mapped.</summary>
    public bool IsTerminal => STATUS is RecordingImportStatus.Attached or RecordingImportStatus.NoRecording or RecordingImportStatus.Failed
        or RecordingImportStatus.NeedsReconnect or RecordingImportStatus.Skipped;

    /// <summary>The instructor may reset the row to <c>Waiting</c> (<see cref="ResetForRetry"/>). Derived — not mapped.</summary>
    public bool CanRetry => STATUS is RecordingImportStatus.Failed or RecordingImportStatus.NoRecording or RecordingImportStatus.NeedsReconnect;

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

    /// <summary>A new import in <see cref="RecordingImportStatus.Waiting"/>: first search at <paramref name="firstSearchAtUtc"/>, give up after <paramref name="searchUntilUtc"/>.</summary>
    public static SESSION_RECORDING_IMPORT Create(
        Guid sessionId, Guid courseId, Guid instructorUserId, DateTime firstSearchAtUtc, DateTime searchUntilUtc)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        if (courseId == Guid.Empty)
        {
            throw new ArgumentException("Course ID cannot be empty.", nameof(courseId));
        }

        if (instructorUserId == Guid.Empty)
        {
            throw new ArgumentException("Instructor user ID cannot be empty.", nameof(instructorUserId));
        }

        return new SESSION_RECORDING_IMPORT
        {
            SESSION_RECORDING_IMPORT_ID = UuidV7.NewId(),
            SESSION_ID = sessionId,
            COURSE_ID = courseId,
            INSTRUCTOR_USER_ID = instructorUserId,
            STATUS = RecordingImportStatus.Waiting,
            ATTEMPTS = 0,
            NEXT_ATTEMPT_AT_UTC = firstSearchAtUtc,
            SEARCH_UNTIL_UTC = searchUntilUtc,
        };
    }

    // ---- Waiting -----------------------------------------------------------------------------------

    /// <summary>Google knows of no finished recording yet: stay <c>Waiting</c> and look again at <paramref name="nextAttemptAtUtc"/>. A successful (if empty) search
    /// ends the run of consecutive failures.</summary>
    public void ScheduleNextSearch(DateTime nextAttemptAtUtc)
    {
        Require(RecordingImportStatus.Waiting);

        ATTEMPTS = 0;
        NEXT_ATTEMPT_AT_UTC = nextAttemptAtUtc;
        ERROR_CODE = null;
    }

    /// <summary>A finished recording was found: remember it and take the transfer lease. <see cref="ATTEMPTS"/> is kept — a transfer that keeps failing must
    /// still run out of attempts even though the search in between succeeds.</summary>
    public void BeginTransfer(string recordingName, string driveFileId, DateTime leaseUntilUtc)
    {
        Require(RecordingImportStatus.Waiting);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingName);
        ArgumentException.ThrowIfNullOrWhiteSpace(driveFileId);

        if (recordingName.Length > GoogleIdMaxLength || driveFileId.Length > GoogleIdMaxLength)
        {
            throw new ArgumentException($"Google ids must be at most {GoogleIdMaxLength} characters.");
        }

        STATUS = RecordingImportStatus.Transferring;
        GOOGLE_RECORDING_NAME = recordingName;
        GOOGLE_FILE_ID = driveFileId;
        LEASE_UNTIL_UTC = leaseUntilUtc;
        NEXT_ATTEMPT_AT_UTC = null;
        ERROR_CODE = null;
    }

    // ---- Transferring ------------------------------------------------------------------------------

    /// <summary>The lease of a <c>Transferring</c> row expired (the run that held it died or hung). Counts as a failed attempt; below the maximum the row
    /// takes a fresh lease and the transfer restarts, at the maximum it ends <c>Failed(transfer_timeout)</c>. Returns <c>true</c> when it failed.</summary>
    public bool ReclaimExpiredLease(DateTime newLeaseUntilUtc, int maxAttempts, IClock clock)
    {
        Require(RecordingImportStatus.Transferring);
        ArgumentNullException.ThrowIfNull(clock);

        ATTEMPTS++;
        if (ATTEMPTS >= maxAttempts)
        {
            Terminate(RecordingImportStatus.Failed, RecordingImportErrorCodes.TransferTimeout, clock);
            return true;
        }

        LEASE_UNTIL_UTC = newLeaseUntilUtc;
        ERROR_CODE = RecordingImportErrorCodes.TransferTimeout;
        return false;
    }

    /// <summary>The copy exists at the video provider as <paramref name="mediaAssetId"/>: wait for transcoding. <paramref name="transcodeDeadlineUtc"/> is kept in
    /// <see cref="LEASE_UNTIL_UTC"/>; the next look is at <paramref name="nextAttemptAtUtc"/>.</summary>
    public void MarkProcessing(Guid mediaAssetId, DateTime nextAttemptAtUtc, DateTime transcodeDeadlineUtc)
    {
        Require(RecordingImportStatus.Transferring);

        if (mediaAssetId == Guid.Empty)
        {
            throw new ArgumentException("Media asset ID cannot be empty.", nameof(mediaAssetId));
        }

        STATUS = RecordingImportStatus.Processing;
        MEDIA_ASSET_ID = mediaAssetId;
        ATTEMPTS = 0;
        NEXT_ATTEMPT_AT_UTC = nextAttemptAtUtc;
        LEASE_UNTIL_UTC = transcodeDeadlineUtc;
        ERROR_CODE = null;
    }

    // ---- Processing --------------------------------------------------------------------------------

    /// <summary>Still transcoding: look again at <paramref name="nextAttemptAtUtc"/>.</summary>
    public void ScheduleNextProcessingCheck(DateTime nextAttemptAtUtc)
    {
        Require(RecordingImportStatus.Processing);

        NEXT_ATTEMPT_AT_UTC = nextAttemptAtUtc;
    }

    /// <summary>The recording is a lesson now.</summary>
    public void MarkAttached(Guid episodeId, IClock clock)
    {
        Require(RecordingImportStatus.Processing);

        if (episodeId == Guid.Empty)
        {
            throw new ArgumentException("Episode ID cannot be empty.", nameof(episodeId));
        }

        EPISODE_ID = episodeId;
        Terminate(RecordingImportStatus.Attached, errorCode: null, clock);
    }

    // ---- Failures ----------------------------------------------------------------------------------

    /// <summary>
    /// A retry-worthy failure (network, 5xx, 429, an ingest error). Increments <see cref="ATTEMPTS"/>; below <paramref name="maxAttempts"/> the row stays alive and is
    /// tried again at <c>now + backoff</c> (a <c>Transferring</c> row goes back to <c>Waiting</c> and releases its lease; <c>Waiting</c>/<c>Processing</c> keep their
    /// status), at the maximum it ends <c>Failed</c>. Returns <c>true</c> when it failed.
    /// </summary>
    public bool RecordTransientFailure(string errorCode, TimeSpan backoff, int maxAttempts, IClock clock)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"A terminal import ({STATUS}) cannot record a failed attempt.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentNullException.ThrowIfNull(clock);

        ATTEMPTS++;
        if (ATTEMPTS >= maxAttempts)
        {
            Terminate(RecordingImportStatus.Failed, errorCode, clock);
            return true;
        }

        ERROR_CODE = Truncate(errorCode);
        NEXT_ATTEMPT_AT_UTC = clock.UtcNow.Add(backoff);
        if (STATUS == RecordingImportStatus.Transferring)
        {
            STATUS = RecordingImportStatus.Waiting;
            LEASE_UNTIL_UTC = null;
        }

        return false;
    }

    /// <summary>Gave up for good (not worth retrying automatically).</summary>
    public void MarkFailed(string errorCode, IClock clock) => TerminateFromActive(RecordingImportStatus.Failed, errorCode, clock);

    /// <summary>The search window ended (or the file is gone from Drive) with no recording → the instructor uploads by hand.</summary>
    public void MarkNoRecording(string errorCode, IClock clock) => TerminateFromActive(RecordingImportStatus.NoRecording, errorCode, clock);

    /// <summary>Google no longer lets the platform read the recording → the instructor must reconnect/consent.</summary>
    public void MarkNeedsReconnect(string errorCode, IClock clock) => TerminateFromActive(RecordingImportStatus.NeedsReconnect, errorCode, clock);

    /// <summary>There is nothing to import (cancelled session, a recording lesson already exists, no Google room, ...).</summary>
    public void MarkSkipped(string errorCode, IClock clock) => TerminateFromActive(RecordingImportStatus.Skipped, errorCode, clock);

    // ---- Instructor retry --------------------------------------------------------------------------

    /// <summary>The instructor asked to try again: back to <c>Waiting</c> with every counter, lease, Google reference and the failed copy forgotten; the next
    /// attempt is due now. Throws unless <see cref="CanRetry"/> — the service answers 409 <c>live.recording_import_not_retryable</c> before getting here.</summary>
    public void ResetForRetry(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (!CanRetry)
        {
            throw new InvalidOperationException($"An import in status {STATUS} cannot be retried.");
        }

        STATUS = RecordingImportStatus.Waiting;
        ATTEMPTS = 0;
        NEXT_ATTEMPT_AT_UTC = clock.UtcNow;
        LEASE_UNTIL_UTC = null;
        GOOGLE_RECORDING_NAME = null;
        GOOGLE_FILE_ID = null;
        MEDIA_ASSET_ID = null;
        EPISODE_ID = null;
        ERROR_CODE = null;
        COMPLETED_AT_UTC = null;
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    private void TerminateFromActive(RecordingImportStatus terminal, string errorCode, IClock clock)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"An import already in a terminal status ({STATUS}) cannot move to {terminal}.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        Terminate(terminal, errorCode, clock);
    }

    private void Terminate(RecordingImportStatus terminal, string? errorCode, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        STATUS = terminal;
        ERROR_CODE = errorCode is null ? null : Truncate(errorCode);
        NEXT_ATTEMPT_AT_UTC = null;
        LEASE_UNTIL_UTC = null;
        COMPLETED_AT_UTC = clock.UtcNow;
    }

    private void Require(RecordingImportStatus expected)
    {
        if (STATUS != expected)
        {
            throw new InvalidOperationException($"The operation needs an import in status {expected} (was {STATUS}).");
        }
    }

    private static string Truncate(string code) => code.Length <= ErrorCodeMaxLength ? code : code[..ErrorCodeMaxLength];
}

/// <summary>The stable codes stored in <see cref="SESSION_RECORDING_IMPORT.ERROR_CODE"/> (also shown to the instructor as <c>errorCode</c>). Never a message,
/// URL, id, token or e-mail.</summary>
public static class RecordingImportErrorCodes
{
    public const string RecordingScopeMissing = "recording_scope_missing";
    public const string InvalidGrant = "invalid_grant";
    public const string GoogleAccountUnavailable = "google_account_unavailable";
    public const string NotWorkspaceAccount = "not_workspace_account";
    public const string NoGoogleMeeting = "no_google_meeting";
    public const string SessionCancelled = "session_cancelled";
    public const string RecordingExists = "recording_exists";
    public const string SessionGone = "session_gone";
    public const string NoRecordingFound = "no_recording_found";
    public const string DriveFileNotFound = "drive_file_not_found";
    public const string FileTooLarge = "file_too_large";
    public const string TransferTimeout = "transfer_timeout";
    public const string IngestFailed = "ingest_failed";
    public const string TranscodeFailed = "transcode_failed";
    public const string TranscodeTimeout = "transcode_timeout";
    public const string AssetMissing = "asset_missing";
    public const string AttachFailed = "attach_failed";
    public const string GoogleTransient = "google_transient";
    public const string GoogleRateLimited = "google_rate_limited";
    public const string GoogleBadRequest = "google_bad_request";
    public const string GoogleClientMisconfigured = "google_client_misconfigured";
    public const string InternalError = "internal_error";
}
