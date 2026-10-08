using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>A live session the caller may see, with the fact that decides how strict the rest of the checks are.</summary>
/// <param name="IsOwner">The caller is the instructor who owns the session's course (not merely an administrator).</param>
public sealed record EntitledSession(LiveSessionContext Context, bool IsOwner);

/// <summary>
/// <b>The join gate</b> — the one place a learner's right to a live room is decided, and the only place (besides the owning instructor's session detail) the
/// raw room link leaves the system (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md section 4.1). Anti link-sharing is the whole point:
/// the link is a capability, so the platform hands it out only to someone who has just proven, server side, that they paid for (or teach) this very session.
/// <para>
/// The order of the checks is fixed — session exists, caller is entitled, session not cancelled, not over, window open, room ready, link readable — and
/// <b>"no such session", "not entitled" and "enrollment expired/revoked" all return the very same <see cref="LiveErrors.NotFound"/></b> so a probe cannot map who
/// is enrolled where. An administrator is <em>not</em> an owner and gets no bypass. Every time decision uses <see cref="IClock"/> and
/// <see cref="LiveSessionDisplayStateCalculator"/>, never a client-supplied time.
/// </para>
/// <para>
/// <b>The log row is committed before the link is returned:</b> <see cref="ISessionJoinLogRepository.SaveChangesAsync"/> is awaited first, and if it fails the
/// exception propagates and no link leaves (the log is the evidence the refund rule reads, so "no log, no link"). Refusals write no join log — only a real reveal
/// does — but each is recorded as a structured <c>live.join.denied</c> line (user id, session id, reason code; never the link, an address or an e-mail).
/// </para>
/// </summary>
public sealed class SessionJoinService(
    ILiveScheduleReader schedule,
    ILearningAccessContract learning,
    ISessionMeetingRepository meetings,
    ISessionJoinLogRepository joinLogs,
    SessionMeetingService meetingService,
    IClock clock,
    IOptions<LiveOptions> options,
    ILogger<SessionJoinService> logger)
{
    /// <summary>The <c>Retry-After</c> (seconds) the controller sends with <see cref="LiveReasons.MeetingNotReady"/>.</summary>
    public const int RetryAfterSeconds = 30;

    /// <summary>
    /// Steps 1 and 2 of the gate, shared with the calendar download: the session must exist and the caller must be its owner or hold an active, unexpired
    /// enrollment of its course. Anything else — unknown id, someone else's session, a lapsed enrollment — is the identical <see cref="LiveErrors.NotFound"/>.
    /// </summary>
    public async Task<Result<EntitledSession>> ResolveEntitledSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var context = (await schedule.GetSessionContextsAsync([sessionId], cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(c => c.SessionId == sessionId);

        if (context is null)
        {
            LogDenied(LogLevel.Warning, userId, sessionId, "no_session");
            return Result.Failure<EntitledSession>(LiveErrors.NotFound);
        }

        var isOwner = context.InstructorUserId == userId;
        var entitled = isOwner
            || await learning.HasActiveEnrollmentAsync(userId, context.CourseId, cancellationToken).ConfigureAwait(false);

        if (!entitled)
        {
            LogDenied(LogLevel.Warning, userId, sessionId, "not_entitled");
            return Result.Failure<EntitledSession>(LiveErrors.NotFound);
        }

        return new EntitledSession(context, isOwner);
    }

    /// <summary>
    /// Runs the gate and, only if every check passes <em>and the join log is committed</em>, returns the room link.
    /// <paramref name="authSessionId"/> is the access token's <c>sid</c> claim, <paramref name="ipAddress"/>/<paramref name="userAgent"/> are forensic data for the log.
    /// </summary>
    public async Task<Result<JoinLiveSessionResponse>> JoinAsync(
        Guid userId,
        Guid sessionId,
        Guid? authSessionId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // 1-2. The session exists and the caller is entitled to it.
        var resolved = await ResolveEntitledSessionAsync(userId, sessionId, cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return Result.Failure<JoinLiveSessionResponse>(resolved.Error);
        }

        var (context, isOwner) = resolved.Value;

        // 3-5. Cancelled, over, or the window is not open yet — decided by the shared calculator so the gate and `my-sessions` can never disagree.
        var window = options.Value.JoinWindowBeforeMinutes;
        var state = LiveSessionDisplayStateCalculator.Compute(context.Status, context.StartsAtUtc, context.EndsAtUtc, now, window);

        switch (state)
        {
            case LiveSessionDisplayState.Cancelled:
                LogDenied(LogLevel.Information, userId, sessionId, "cancelled");
                return Result.Failure<JoinLiveSessionResponse>(LiveErrors.SessionCancelled());

            case LiveSessionDisplayState.Ended:
                LogDenied(LogLevel.Information, userId, sessionId, "ended");
                return Result.Failure<JoinLiveSessionResponse>(LiveErrors.SessionEnded(context, withRecordingHint: true));

            case LiveSessionDisplayState.Upcoming when !isOwner:
                // The owner may enter the room early to prepare; learners wait for the window.
                LogDenied(LogLevel.Information, userId, sessionId, "window_not_open");
                return Result.Failure<JoinLiveSessionResponse>(LiveErrors.WindowNotOpen(context.StartsAtUtc.AddMinutes(-window), now));
        }

        // 6. A room that can be entered.
        var meeting = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (meeting is null || !meeting.IsUsable)
        {
            LogDenied(LogLevel.Warning, userId, sessionId, "meeting_not_ready");
            return Result.Failure<JoinLiveSessionResponse>(LiveErrors.MeetingNotReady());
        }

        // 7. The stored link, decrypted. RevealUrl never throws for an unreadable ciphertext (it logs the session id and returns null) — the answer is
        // the same "room not ready", never a 500 that could carry anything.
        var url = meetingService.RevealUrl(meeting);
        if (string.IsNullOrEmpty(url))
        {
            LogDenied(LogLevel.Error, userId, sessionId, "url_unreadable");
            return Result.Failure<JoinLiveSessionResponse>(LiveErrors.MeetingNotReady());
        }

        // 8. Commit the evidence first. If this throws, the link was never put into any return value.
        joinLogs.Add(SESSION_JOIN_LOG.Record(
            sessionId,
            context.CourseId,
            userId,
            isOwner ? LiveParticipantRole.Instructor : LiveParticipantRole.Learner,
            authSessionId,
            now,
            ipAddress,
            userAgent));
        await joinLogs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 9. Only now does the link leave.
        return new JoinLiveSessionResponse(sessionId, url, context.StartsAtUtc, context.EndsAtUtc, now);
    }

    /// <summary>Structured log of a refused join. Carries only ids and a reason code — never the room link, an IP address or an e-mail.</summary>
    private void LogDenied(LogLevel level, Guid userId, Guid sessionId, string reason) =>
        logger.Log(level, "live.join.denied userId={UserId} sessionId={SessionId} reason={Reason}", userId, sessionId, reason);
}
