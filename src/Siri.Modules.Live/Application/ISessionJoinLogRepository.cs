using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

/// <summary>One learner's history with one session: when they first got the room link and how many times they asked for it.</summary>
public sealed record LearnerJoinSummary(DateTime FirstJoinedAtUtc, int JoinCount);

/// <summary>
/// Persistence abstraction for the append-only <see cref="SESSION_JOIN_LOG"/> (docs/contracts/
/// P11-05-live-learner-instructor-api-join-gate.md section 2), implemented by <c>Infrastructure.SessionJoinLogRepository</c>.
/// There is deliberately no update or delete: a row is added once, at the moment the room link is revealed, and the refund rule reads it.
/// </summary>
public interface ISessionJoinLogRepository
{
    /// <summary>Stages a new row; nothing is written until <see cref="SaveChangesAsync"/>.</summary>
    void Add(SESSION_JOIN_LOG log);

    /// <summary>Commits staged rows. The join gate awaits this <b>before</b> it returns the room link — if it throws, no link leaves.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Distinct users who were handed the room link of <paramref name="sessionId"/> in the learner role.</summary>
    Task<IReadOnlyList<Guid>> GetJoinedLearnerIdsAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>First-join time and join count per learner of one session, for the given users only (learner role; users without a row are absent).</summary>
    Task<IReadOnlyDictionary<Guid, LearnerJoinSummary>> GetLearnerJoinSummariesAsync(
        Guid sessionId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}
