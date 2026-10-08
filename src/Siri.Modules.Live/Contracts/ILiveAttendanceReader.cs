namespace Siri.Modules.Live.Contracts;

/// <summary>
/// Attendance numbers of one live session (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md section 3.1).
/// </summary>
/// <param name="ExpectedLearners">Learners whose calendar invitation is <c>Invited</c> (the people the platform told about the session).</param>
/// <param name="JoinedLearners">Distinct learners who were handed the room link (<c>SESSION_JOIN_LOGS</c> rows with the learner role).</param>
/// <param name="MeetingUsable">The session has a room that can be entered right now (a stored link on a meeting that is not deleted).</param>
public sealed record LiveSessionStats(Guid SessionId, int ExpectedLearners, int JoinedLearners, bool MeetingUsable);

/// <summary>
/// Read-only attendance facts the Live module publishes to other modules: Commerce (refund hard-block after a live join, P11-12) and
/// Analytics (instructor dashboard, P11-10). Both methods are batch queries — callers must pass every id at once, never loop.
/// </summary>
public interface ILiveAttendanceReader
{
    /// <summary>
    /// Which of <paramref name="courseIds"/> the user has ever been handed a room link for <b>as a learner</b> (an instructor's own
    /// joins never count). The refund rule asks this by (user, course) without joining across modules.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetCourseIdsAttendedAsync(
        Guid userId,
        IReadOnlyCollection<Guid> courseIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stats for <paramref name="sessionIds"/> in one pass. Every requested id is present in the result — a session nobody joined or was
    /// invited to reports zeros, and a session without a meeting row reports <c>MeetingUsable = false</c>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, LiveSessionStats>> GetSessionStatsAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken);
}
