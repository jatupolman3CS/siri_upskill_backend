using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>
/// The instructor-facing side of the automatic recording import (aggregate: <see cref="SESSION_RECORDING_IMPORT"/>) — what each session shows
/// (<see cref="RecordingImportInfo"/>) and the instructor's <b>retry</b> (docs/contracts/P11-13-live-recording-auto-import.md sections 6-7). The import itself
/// is driven by <c>LiveRecordingImportJob</c>; this service never calls Google.
/// <para>
/// <b>Ownership:</b> the caller's user id comes from the authenticated principal; a session is theirs only if <c>LiveSessionContext.InstructorUserId</c> equals
/// it — an administrator is <em>not</em> an owner. As everywhere on the instructor side a session is not a secret from its own teacher group: unknown = 404,
/// someone else's = 403. Nothing returned here ever contains a Drive id, an asset id, a URL or a message — only codes and times.
/// </para>
/// </summary>
public sealed class RecordingImportService(
    ISessionRecordingImportRepository imports,
    IInstructorGoogleAccountRepository accounts,
    ISessionMeetingRepository meetings,
    ILiveScheduleReader schedule,
    IClock clock,
    IOptions<LiveOptions> options,
    ILogger<RecordingImportService> logger)
{
    /// <summary>How long after a class ended the instructor may still ask for the import to be tried again (contract section 6).</summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromDays(30);

    private static readonly DomainError SessionNotFoundError = DomainError.NotFound("ไม่พบคาบสอนนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์เข้าถึงคาบสอนนี้");

    /// <summary>The master switch (<c>Live:Recording:AutoImport:Enabled</c>).</summary>
    public bool Enabled => options.Value.Recording.AutoImport.Enabled;

    // ---- Capability and per-session info --------------------------------------------------------------

    /// <summary>What the platform can do about recordings for this instructor (reads their Google account row; never calls Google).</summary>
    public async Task<RecordingCapability> GetCapabilityAsync(Guid instructorUserId, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByInstructorUserIdAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        return RecordingCapabilityCalculator.Compute(Enabled, options.Value.Provider, account);
    }

    /// <summary>The <c>recordingImport</c> member for each of <paramref name="contexts"/> (all of them the caller's own sessions), keyed by session id.
    /// <paramref name="rooms"/> are the sessions' meeting rows already loaded by the caller (a session without one has no Google room).</summary>
    public async Task<IReadOnlyDictionary<Guid, RecordingImportInfo>> GetInfosAsync(
        Guid instructorUserId,
        IReadOnlyCollection<LiveSessionContext> contexts,
        IReadOnlyDictionary<Guid, SESSION_MEETING> rooms,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(rooms);

        if (contexts.Count == 0)
        {
            return new Dictionary<Guid, RecordingImportInfo>();
        }

        var capability = await GetCapabilityAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        var rows = (await imports.GetBySessionIdsAsync(contexts.Select(c => c.SessionId).ToArray(), cancellationToken).ConfigureAwait(false))
            .ToDictionary(r => r.SESSION_ID);

        return contexts.ToDictionary(
            c => c.SessionId,
            c => ToInfo(
                rows.GetValueOrDefault(c.SessionId),
                capability,
                c,
                RecordingImportRules.HasGoogleRoom(rooms.GetValueOrDefault(c.SessionId)),
                Enabled,
                clock.UtcNow));
    }

    /// <summary>The <c>recordingImport</c> member of one session (the detail view).</summary>
    public async Task<RecordingImportInfo> GetInfoAsync(Guid instructorUserId, LiveSessionContext context, SESSION_MEETING? room, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var capability = await GetCapabilityAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        var rows = await imports.GetBySessionIdsAsync([context.SessionId], cancellationToken).ConfigureAwait(false);

        return ToInfo(rows.FirstOrDefault(), capability, context, RecordingImportRules.HasGoogleRoom(room), Enabled, clock.UtcNow);
    }

    /// <summary>Pure mapping (unit-tested): the DTO for one session from its import row (if any), the instructor's capability and the session facts.</summary>
    public static RecordingImportInfo ToInfo(
        SESSION_RECORDING_IMPORT? row,
        RecordingCapability capability,
        LiveSessionContext context,
        bool hasGoogleRoom,
        bool autoImportEnabled,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Contract section 2: with the feature off the mode is Manual for every session, even where an old import row exists (the row's status and error code are
        // still reported - it is history the instructor may want to read - but nothing can be retried, see CanRetry).
        var mode = autoImportEnabled && (row is not null || capability == RecordingCapability.Auto) ? RecordingImportMode.Auto : RecordingImportMode.Manual;

        return new RecordingImportInfo(
            mode,
            row?.STATUS,
            row?.STATUS == RecordingImportStatus.Attached ? null : row?.ERROR_CODE,
            row?.STATUS is RecordingImportStatus.Waiting or RecordingImportStatus.Processing ? row.NEXT_ATTEMPT_AT_UTC : null,
            CanRetry(row, capability, context, hasGoogleRoom, autoImportEnabled, nowUtc));
    }

    /// <summary>
    /// Whether <see cref="RetryAsync"/> would succeed (contract section 6): the feature is on, the class is over, was not cancelled, has no recording lesson yet and ended
    /// within <see cref="RetryWindow"/>; and either the import is <c>Failed</c>/<c>NoRecording</c>/<c>NeedsReconnect</c>, or there is no import at all while the instructor's
    /// capability is <c>Auto</c> and the class has a Google room.
    /// </summary>
    public static bool CanRetry(
        SESSION_RECORDING_IMPORT? row,
        RecordingCapability capability,
        LiveSessionContext context,
        bool hasGoogleRoom,
        bool autoImportEnabled,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!autoImportEnabled
            || context.Status == LiveSessionStatus.Cancelled
            || context.RecordingEpisodeId is not null
            || nowUtc <= context.EndsAtUtc
            || nowUtc > context.EndsAtUtc + RetryWindow)
        {
            return false;
        }

        return row is not null
            ? row.CanRetry
            : capability == RecordingCapability.Auto && hasGoogleRoom;
    }

    // ---- Retry ----------------------------------------------------------------------------------------

    /// <summary>
    /// The instructor asks for the import of <paramref name="sessionId"/> to be tried again: a <c>Failed</c>/<c>NoRecording</c>/<c>NeedsReconnect</c> import is reset to
    /// <c>Waiting</c> (attempts cleared, next attempt now); a session with no import whose instructor has <c>Auto</c> capability gets a <c>Waiting</c> one immediately.
    /// 404 unknown session, 403 someone else's, 409 <c>live.recording_import_not_retryable</c> otherwise (also on a lost race with the job).
    /// </summary>
    public async Task<Result<RecordingImportInfo>> RetryAsync(Guid instructorUserId, Guid sessionId, CancellationToken cancellationToken)
    {
        var context = (await schedule.GetSessionContextsAsync([sessionId], cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(c => c.SessionId == sessionId);

        if (context is null)
        {
            return Result.Failure<RecordingImportInfo>(SessionNotFoundError);
        }

        if (context.InstructorUserId != instructorUserId)
        {
            return Result.Failure<RecordingImportInfo>(NotOwnerError);
        }

        var row = await imports.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var capability = await GetCapabilityAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        var room = (await meetings.GetBySessionIdsAsync([sessionId], cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        var hasGoogleRoom = RecordingImportRules.HasGoogleRoom(room);
        var now = clock.UtcNow;

        if (!CanRetry(row, capability, context, hasGoogleRoom, Enabled, now))
        {
            return Result.Failure<RecordingImportInfo>(LiveErrors.RecordingImportNotRetryable);
        }

        if (row is null)
        {
            row = SESSION_RECORDING_IMPORT.Create(
                context.SessionId,
                context.CourseId,
                context.InstructorUserId,
                firstSearchAtUtc: now,
                searchUntilUtc: context.EndsAtUtc.AddHours(options.Value.Recording.AutoImport.SearchWindowHours));
            imports.Add(row);
        }
        else
        {
            row.ResetForRetry(clock);
        }

        try
        {
            await imports.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or DbUpdateException)
        {
            // The job (or another tab) changed or created the row between our read and our write: the retry is moot.
            logger.LogInformation("Recording import retry for session {SessionId} lost a race ({ExceptionType}).", sessionId, ex.GetType().Name);
            imports.ClearTracking();
            return Result.Failure<RecordingImportInfo>(LiveErrors.RecordingImportNotRetryable);
        }

        return ToInfo(row, capability, context, hasGoogleRoom, Enabled, now);
    }
}

/// <summary>Small shared rules of the recording import (kept out of the entities because they look across modules' rows).</summary>
public static class RecordingImportRules
{
    /// <summary>The session's room is a Google Meet space the platform created for it (<c>GoogleMeet</c>, or the dev <c>Logging</c> fake) and still exists — the only
    /// kind of room whose recording can be found automatically. A pasted Zoom/Teams/Meet link is somebody else's room.</summary>
    public static bool HasGoogleRoom(SESSION_MEETING? meeting) =>
        meeting is { MEET_URL_ENCRYPTED: not null, SYNC_STATUS: not MeetingSyncStatus.Deleted, PROVIDER: MeetingProvider.GoogleMeet or MeetingProvider.Logging };
}
