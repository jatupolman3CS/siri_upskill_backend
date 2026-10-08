using Microsoft.EntityFrameworkCore;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Persistence;

namespace Siri.Modules.Live.Infrastructure;

public sealed class SessionInviteRepository(AppDbContext context) : ISessionInviteRepository
{
    public async Task<IReadOnlyList<SESSION_INVITE>> GetBySessionIdsAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        // Uses IX_SESSION_INVITES_SESSION_USER (leading column SESSION_ID). Tracked: the caller mutates and saves.
        return await context.SessionInvites()
            .Where(i => ids.Contains(i.SESSION_ID))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SESSION_INVITE>> GetInvitedAwaitingReminderAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await context.SessionInvites()
            .Where(i => ids.Contains(i.SESSION_ID)
                && i.STATUS == InviteStatus.Invited
                && (i.REMINDER_24H_SENT_AT_UTC == null || i.REMINDER_1H_SENT_AT_UTC == null))
            .OrderBy(i => i.SESSION_ID)
            .ThenBy(i => i.USER_ID)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> GetSessionIdsNeedingAttendeeSyncAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        // Uses IX_SESSION_INVITES_SESSION_USER (leading column SESSION_ID); ids only, nothing tracked.
        return await context.SessionInvites()
            .AsNoTracking()
            .Where(i => ids.Contains(i.SESSION_ID)
                && i.ROLE == LiveParticipantRole.Learner
                && ((i.STATUS == InviteStatus.Invited && i.GOOGLE_ATTENDEE_SYNCED_AT_UTC == null)
                    || (i.STATUS != InviteStatus.Invited && i.GOOGLE_ATTENDEE_SYNCED_AT_UTC != null)))
            .Select(i => i.SESSION_ID)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(SESSION_INVITE invite) => context.SessionInvites().Add(invite);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    public void ClearTracking() => context.ChangeTracker.Clear();
}
