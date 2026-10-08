using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>
/// The instructor-facing operations on a session's online room (aggregate: <see cref="SESSION_MEETING"/>) — list a course's
/// rooms, paste a manual link, ask for a retry — plus the one place a stored room URL is decrypted (<see cref="RevealUrl"/>)
/// (P11-03 contract section 6.2). Cross-module facts (who owns a course/session, when it runs) come through
/// <see cref="ICatalogPriceContract"/> / <see cref="ILiveScheduleReader"/> only.
/// <para>
/// <b>Authorisation:</b> the caller's user id always comes from the authenticated principal (the controller passes it in); an
/// administrator is <em>not</em> an owner. A course the caller does not own is answered exactly like a missing one (404); for a
/// session a non-owner gets 403 and an unknown id 404, as the contract specifies.
/// </para>
/// <para>
/// The room URL never leaves this service except through <see cref="RevealUrl"/>; summaries expose only <c>HasMeetingLink</c>, and the
/// log line of a manual link carries the session id and host, never the URL.
/// </para>
/// </summary>
public sealed class SessionMeetingService(
    ISessionMeetingRepository meetings,
    ILiveScheduleReader schedule,
    ICatalogPriceContract catalog,
    MeetingLinkValidator linkValidator,
    ISensitiveDataProtector protector,
    IClock clock,
    ILogger<SessionMeetingService> logger)
{
    public const string SessionNotEditableReason = "live.session_not_editable";

    public const string MeetingNotResyncableReason = "live.meeting_not_resyncable";

    // ---- Queries ----------------------------------------------------------------------------------

    /// <summary>The rooms of every session of a course the caller owns, in schedule order. Sessions that have no meeting row yet are
    /// simply absent (the row appears within a minute of the session being created).</summary>
    public async Task<Result<CourseMeetingsResponse>> GetCourseMeetingSummariesAsync(
        Guid courseId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await catalog.IsInstructorOwnerOfCourseAsync(courseId, userId, cancellationToken).ConfigureAwait(false))
        {
            // Not the owner and "no such course" are deliberately indistinguishable.
            return Result.Failure<CourseMeetingsResponse>(DomainError.NotFound("ไม่พบคอร์สนี้"));
        }

        var sessions = await schedule.GetSessionsForCourseAsync(courseId, cancellationToken).ConfigureAwait(false);
        if (sessions.Count == 0)
        {
            return new CourseMeetingsResponse([]);
        }

        var rows = (await meetings.GetBySessionIdsAsync(sessions.Select(s => s.SessionId).ToArray(), cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => m.SESSION_ID);

        var items = sessions
            .Where(s => rows.ContainsKey(s.SessionId))
            .Select(s => ToSummary(rows[s.SessionId]))
            .ToList();

        return new CourseMeetingsResponse(items);
    }

    // ---- Commands ---------------------------------------------------------------------------------

    /// <summary>
    /// Stores a room link the instructor pasted. The URL must pass <see cref="MeetingLinkValidator"/>; the class must still be
    /// scheduled and not finished. It is encrypted before it touches the entity, any Google event the meeting had is queued for
    /// deletion, and the response says only that a link exists.
    /// </summary>
    public async Task<Result<InstructorMeetingSummary>> SetManualLinkAsync(
        Guid userId, Guid sessionId, string? url, CancellationToken cancellationToken)
    {
        var contextResult = await GetOwnedSessionAsync(userId, sessionId, cancellationToken).ConfigureAwait(false);
        if (contextResult.IsFailure)
        {
            return Result.Failure<InstructorMeetingSummary>(contextResult.Error);
        }

        var context = contextResult.Value;
        if (context.Status != LiveSessionStatus.Scheduled || context.EndsAtUtc <= clock.UtcNow)
        {
            return Result.Failure<InstructorMeetingSummary>(NotEditable());
        }

        var validated = linkValidator.Validate(url);
        if (validated.IsFailure)
        {
            return Result.Failure<InstructorMeetingSummary>(validated.Error);
        }

        var meeting = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (meeting is null)
        {
            meeting = SESSION_MEETING.Stage(sessionId);
            meetings.Add(meeting);
        }

        if (meeting.SYNC_STATUS == MeetingSyncStatus.Deleted)
        {
            return Result.Failure<InstructorMeetingSummary>(NotEditable());
        }

        if (meeting.INSTRUCTOR_USER_ID is null)
        {
            meeting.AssignInstructor(context.InstructorUserId);
        }

        meeting.SetManualLink(protector.Encrypt(validated.Value));

        try
        {
            await meetings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<InstructorMeetingSummary>(ConcurrentChange());
        }

        // Host only — the path/query of a meeting URL can be a secret.
        logger.LogInformation(
            "Manual meeting link set for live session {SessionId} (host {Host}).",
            sessionId,
            new Uri(validated.Value).Host);

        return ToSummary(meeting);
    }

    /// <summary>Asks the sync job to try again for a room that is stuck (failed, waiting for a link, or waiting for a reconnect).</summary>
    public async Task<Result<InstructorMeetingSummary>> ResyncAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var contextResult = await GetOwnedSessionAsync(userId, sessionId, cancellationToken).ConfigureAwait(false);
        if (contextResult.IsFailure)
        {
            return Result.Failure<InstructorMeetingSummary>(contextResult.Error);
        }

        var meeting = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (meeting is null)
        {
            return Result.Failure<InstructorMeetingSummary>(DomainError.NotFound("ไม่พบห้องประชุมของคาบนี้"));
        }

        if (!meeting.CanRequestResync)
        {
            return Result.Failure<InstructorMeetingSummary>(
                DomainError.Conflict("ห้องประชุมนี้ไม่ต้องสร้างใหม่ในขณะนี้").WithReason(MeetingNotResyncableReason));
        }

        meeting.RequestResync();

        try
        {
            await meetings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<InstructorMeetingSummary>(ConcurrentChange());
        }

        return ToSummary(meeting);
    }

    // ---- Mapping / reveal -------------------------------------------------------------------------

    /// <summary>
    /// Decrypts the stored room URL, or <c>null</c> when the meeting has none or it cannot be read. <b>The only place a stored URL is
    /// decrypted; callers must never log the result.</b> Used by the join gate and the owning instructor's session detail (P11-05).
    /// </summary>
    public string? RevealUrl(SESSION_MEETING meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);

        if (string.IsNullOrEmpty(meeting.MEET_URL_ENCRYPTED))
        {
            return null;
        }

        try
        {
            return protector.Decrypt(meeting.MEET_URL_ENCRYPTED);
        }
        // CryptographicException: wrong key / tampered; FormatException: not Base64; InvalidOperationException: SensitiveDataProtector reports a ciphertext too
        // short to hold a nonce and tag (a truncated/corrupted column) — all of them mean "this room cannot be read", never a 500 for the join gate (P11-05 section 4.1 step 7).
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or InvalidOperationException)
        {
            // Never the ciphertext, never the session's URL — just that this meeting is unreadable.
            logger.LogError("The stored meeting link of live session {SessionId} could not be decrypted ({ExceptionType}).", meeting.SESSION_ID, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>The instructor-facing view of a meeting. <b>Never includes the URL.</b></summary>
    public static InstructorMeetingSummary ToSummary(SESSION_MEETING meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);

        var hasLink = !string.IsNullOrEmpty(meeting.MEET_URL_ENCRYPTED);

        return new InstructorMeetingSummary(
            meeting.SESSION_ID,
            meeting.PROVIDER,
            meeting.SYNC_STATUS,
            hasLink,
            meeting.IsUsable,
            meeting.LAST_SYNC_AT_UTC,
            DeriveNeedsAction(meeting.SYNC_STATUS, hasLink),
            meeting.ERROR);
    }

    /// <summary>What the instructor must do next (docs/contracts/P11-FE-live-dto-appendix.md A.5 — exact table).</summary>
    public static MeetingNeedsAction DeriveNeedsAction(MeetingSyncStatus status, bool hasMeetingLink) => status switch
    {
        MeetingSyncStatus.Pending => MeetingNeedsAction.Waiting,
        MeetingSyncStatus.AwaitingLink => MeetingNeedsAction.PasteLink,
        MeetingSyncStatus.NeedsReconnect => MeetingNeedsAction.ReconnectGoogle,
        MeetingSyncStatus.Failed => MeetingNeedsAction.Retry,
        MeetingSyncStatus.Synced => hasMeetingLink ? MeetingNeedsAction.None : MeetingNeedsAction.PasteLink,
        MeetingSyncStatus.PendingDelete or MeetingSyncStatus.Deleted => MeetingNeedsAction.None,
        _ => MeetingNeedsAction.None,
    };

    // ---- Helpers ----------------------------------------------------------------------------------

    /// <summary>The session's context if it exists and belongs to <paramref name="userId"/>; 404 when unknown, 403 when someone else's.</summary>
    private async Task<Result<LiveSessionContext>> GetOwnedSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var context = (await schedule.GetSessionContextsAsync([sessionId], cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(c => c.SessionId == sessionId);

        if (context is null)
        {
            return Result.Failure<LiveSessionContext>(DomainError.NotFound("ไม่พบคาบสอนนี้"));
        }

        if (context.InstructorUserId != userId)
        {
            return Result.Failure<LiveSessionContext>(DomainError.Forbidden("คุณไม่มีสิทธิ์จัดการห้องประชุมของคาบนี้"));
        }

        return context;
    }

    private static DomainError NotEditable() =>
        DomainError.Conflict("แก้ไขห้องประชุมได้เฉพาะคาบที่ยังไม่ถูกยกเลิกและยังไม่จบ").WithReason(SessionNotEditableReason);

    private static DomainError ConcurrentChange() =>
        DomainError.Conflict("ข้อมูลห้องประชุมถูกเปลี่ยนแปลงพร้อมกัน กรุณาลองใหม่อีกครั้ง");
}
