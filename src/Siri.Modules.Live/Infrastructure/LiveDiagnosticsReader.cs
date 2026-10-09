using Microsoft.EntityFrameworkCore;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Persistence;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// Counts for the admin status endpoint. Read-only (<c>AsNoTracking</c>), one small query per number, each capped at
/// <see cref="ILiveDiagnosticsReader.CountCap"/>: the meeting counts filter on <c>SYNC_STATUS</c> (the partial index
/// <c>IX_SESSION_MEETINGS_SYNC_DUE</c> covers the pending ones), the invite count on <c>STATUS</c> over a table the reconcile job keeps near-empty of pending rows.
/// Nothing but integers is returned.
/// </summary>
public sealed class LiveDiagnosticsReader(AppDbContext dbContext) : ILiveDiagnosticsReader
{
    public async Task<LiveOperationalCounts> GetCountsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var meetings = dbContext.SessionMeetings().AsNoTracking();

        var pending = await meetings
            .Where(m => m.SYNC_STATUS == MeetingSyncStatus.Pending || m.SYNC_STATUS == MeetingSyncStatus.PendingDelete)
            .Take(ILiveDiagnosticsReader.CountCap)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var awaitingLink = await meetings
            .Where(m => m.SYNC_STATUS == MeetingSyncStatus.AwaitingLink)
            .Take(ILiveDiagnosticsReader.CountCap)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var failed = await meetings
            .Where(m => m.SYNC_STATUS == MeetingSyncStatus.Failed)
            .Take(ILiveDiagnosticsReader.CountCap)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        // Stuck = the job should have handled it long ago: it is due now (no future retry time) and has not been touched for a while.
        var stuckBefore = nowUtc - ILiveDiagnosticsReader.StuckPendingAfter;
        var stuck = await meetings
            .Where(m => (m.SYNC_STATUS == MeetingSyncStatus.Pending || m.SYNC_STATUS == MeetingSyncStatus.PendingDelete)
                && (m.NEXT_RETRY_AT_UTC == null || m.NEXT_RETRY_AT_UTC <= nowUtc)
                && (m.UpdatedAtUtc ?? m.CreatedAtUtc) <= stuckBefore)
            .Take(ILiveDiagnosticsReader.CountCap)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var invitesPending = await dbContext.SessionInvites()
            .AsNoTracking()
            .Where(i => i.STATUS == InviteStatus.Pending)
            .Take(ILiveDiagnosticsReader.CountCap)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        return new LiveOperationalCounts(pending, awaitingLink, failed, stuck, invitesPending);
    }
}
