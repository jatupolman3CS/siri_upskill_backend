using Microsoft.EntityFrameworkCore;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Persistence;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// EF implementation of <see cref="ISessionJoinLogRepository"/>. Append-only: it can add rows and read them, nothing else. The reads are all batched,
/// untracked, and served by <c>IX_SESSION_JOIN_LOGS_SESSION_USER</c> (session-scoped) — there is no query that scans the table by anything unindexed.
/// </summary>
public sealed class SessionJoinLogRepository(AppDbContext context) : ISessionJoinLogRepository
{
    public void Add(SESSION_JOIN_LOG log)
    {
        ArgumentNullException.ThrowIfNull(log);
        context.SessionJoinLogs().Add(log);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetJoinedLearnerIdsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return await context.SessionJoinLogs()
            .AsNoTracking()
            .Where(l => l.SESSION_ID == sessionId && l.ROLE == LiveParticipantRole.Learner)
            .Select(l => l.USER_ID)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, LearnerJoinSummary>> GetLearnerJoinSummariesAsync(
        Guid sessionId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, LearnerJoinSummary>();
        }

        var rows = await context.SessionJoinLogs()
            .AsNoTracking()
            .Where(l => l.SESSION_ID == sessionId && l.ROLE == LiveParticipantRole.Learner && ids.Contains(l.USER_ID))
            .GroupBy(l => l.USER_ID)
            .Select(g => new { UserId = g.Key, First = g.Min(l => l.JOINED_AT_UTC), Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.UserId, r => new LearnerJoinSummary(r.First, r.Count));
    }
}
