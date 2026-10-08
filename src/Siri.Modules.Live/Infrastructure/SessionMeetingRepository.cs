using Microsoft.EntityFrameworkCore;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Persistence;

namespace Siri.Modules.Live.Infrastructure;

public sealed class SessionMeetingRepository(AppDbContext context) : ISessionMeetingRepository
{
    public async Task<SESSION_MEETING?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        // A meeting staged earlier in the same unit of work is not in the database yet — look at the tracker first.
        var local = context.SessionMeetings().Local.FirstOrDefault(m => m.SESSION_ID == sessionId);
        if (local is not null)
        {
            return local;
        }

        return await context.SessionMeetings()
            .FirstOrDefaultAsync(m => m.SESSION_ID == sessionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SESSION_MEETING>> GetBySessionIdsAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await context.SessionMeetings()
            .AsNoTracking()
            .Where(m => ids.Contains(m.SESSION_ID))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<Guid>> GetExistingSessionIdsAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new HashSet<Guid>();
        }

        var existing = await context.SessionMeetings()
            .AsNoTracking()
            .Where(m => ids.Contains(m.SESSION_ID))
            .Select(m => m.SESSION_ID)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return existing.ToHashSet();
    }

    public async Task<IReadOnlyList<Guid>> GetDueSessionIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken)
    {
        // Uses IX_SESSION_MEETINGS_SYNC_DUE (partial index on Pending/PendingDelete).
        return await context.SessionMeetings()
            .AsNoTracking()
            .Where(m => (m.SYNC_STATUS == MeetingSyncStatus.Pending || m.SYNC_STATUS == MeetingSyncStatus.PendingDelete)
                && (m.NEXT_RETRY_AT_UTC == null || m.NEXT_RETRY_AT_UTC <= nowUtc))
            .OrderBy(m => m.UpdatedAtUtc ?? m.CreatedAtUtc)
            .ThenBy(m => m.SESSION_MEETING_ID)
            .Take(Math.Max(take, 1))
            .Select(m => m.SESSION_ID)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SESSION_MEETING>> GetResettableByInstructorAsync(
        Guid instructorUserId, CancellationToken cancellationToken)
    {
        // Uses IX_SESSION_MEETINGS_INSTR_USER_ID.
        return await context.SessionMeetings()
            .Where(m => m.INSTRUCTOR_USER_ID == instructorUserId
                && m.MEET_URL_ENCRYPTED == null
                && (m.SYNC_STATUS == MeetingSyncStatus.AwaitingLink
                    || m.SYNC_STATUS == MeetingSyncStatus.NeedsReconnect
                    || m.SYNC_STATUS == MeetingSyncStatus.Failed))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(SESSION_MEETING meeting) => context.SessionMeetings().Add(meeting);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    public void ClearTracking() => context.ChangeTracker.Clear();
}
