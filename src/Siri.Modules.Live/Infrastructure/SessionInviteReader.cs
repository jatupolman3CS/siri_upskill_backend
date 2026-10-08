using Microsoft.EntityFrameworkCore;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Persistence;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>EF implementation of <see cref="ISessionInviteReader"/> — read-only, untracked, batched.</summary>
public sealed class SessionInviteReader(AppDbContext context) : ISessionInviteReader
{
    public async Task<IReadOnlyDictionary<Guid, InviteStatus>> GetStatusesForUserAsync(
        Guid userId,
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, InviteStatus>();
        }

        // Uses IX_SESSION_INVITES_USER_ID; UNIQUE (SESSION_ID, USER_ID) guarantees at most one row per session.
        var rows = await context.SessionInvites()
            .AsNoTracking()
            .Where(i => i.USER_ID == userId && ids.Contains(i.SESSION_ID))
            .Select(i => new { i.SESSION_ID, i.STATUS })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.SESSION_ID, r => r.STATUS);
    }

    public async Task<IReadOnlyDictionary<Guid, InviteStatus>> GetLearnerStatusesForSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        // Uses IX_SESSION_INVITES_SESSION_USER (leading column SESSION_ID).
        var rows = await context.SessionInvites()
            .AsNoTracking()
            .Where(i => i.SESSION_ID == sessionId && i.ROLE == LiveParticipantRole.Learner)
            .Select(i => new { i.USER_ID, i.STATUS })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.USER_ID, r => r.STATUS);
    }
}
