namespace Siri.Modules.Live.Application;

/// <summary>
/// Operator-facing counts of the Live module's background work (admin diagnostics). Numbers only — never a session, a user, an
/// address or a URL.
/// </summary>
/// <param name="MeetingsPending">Rooms still waiting for the sync job (<c>Pending</c> / <c>PendingDelete</c>). Capped at <see cref="ILiveDiagnosticsReader.CountCap"/>.</param>
/// <param name="MeetingsAwaitingLink">Rooms waiting for the instructor to paste a link.</param>
/// <param name="MeetingsFailed">Rooms the job gave up on after every retry.</param>
/// <param name="MeetingsStuckPending">Of <paramref name="MeetingsPending"/>: due for processing and untouched for longer than
/// <see cref="ILiveDiagnosticsReader.StuckPendingAfter"/> — the sync job runs every minute, so these are not merely queued, they are not being processed.</param>
/// <param name="InvitesPending">Invitations recorded but not sent yet.</param>
public sealed record LiveOperationalCounts(
    int MeetingsPending,
    int MeetingsAwaitingLink,
    int MeetingsFailed,
    int MeetingsStuckPending,
    int InvitesPending);

/// <summary>Read-only, bounded counters over the Live module's tables for the admin status endpoint.</summary>
public interface ILiveDiagnosticsReader
{
    /// <summary>Every count is capped at this value so the queries stay cheap however large a table grows.</summary>
    const int CountCap = 10_000;

    /// <summary>A due <c>Pending</c> meeting untouched this long is considered stuck (the sync job runs every minute).</summary>
    static readonly TimeSpan StuckPendingAfter = TimeSpan.FromMinutes(10);

    Task<LiveOperationalCounts> GetCountsAsync(DateTime nowUtc, CancellationToken cancellationToken);
}
