using Microsoft.EntityFrameworkCore;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.Persistence;

namespace Siri.Modules.Media.Infrastructure;

/// <summary>EF Core-backed <see cref="IPlaybackSessionRepository"/> — see <see cref="MediaAssetRepository"/>'s
/// own doc comment for why this is a real implementation, not stubbed. No <c>Remove</c>/update method: rows
/// are append-only (see <see cref="PLAYBACK_SESSION"/>'s own doc comment).</summary>
public sealed class PlaybackSessionRepository(AppDbContext dbContext) : IPlaybackSessionRepository
{
    public Task<PLAYBACK_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.PlaybackSessions().AsNoTracking().FirstOrDefaultAsync(p => p.PLAYBACK_SESSION_ID == id, cancellationToken);

    public async Task<(IReadOnlyList<PLAYBACK_SESSION> Items, int TotalCount)> GetPagedByUserIdAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.PlaybackSessions()
            .AsNoTracking()
            .Where(p => p.USER_ID == userId)
            .OrderByDescending(p => p.ISSUED_AT_UTC);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<PlaybackUserActivity>> GetUserActivitySinceAsync(
        DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var rawSessions = await dbContext.PlaybackSessions()
            .AsNoTracking()
            .Where(p => p.ISSUED_AT_UTC >= sinceUtc)
            .Select(p => new { p.USER_ID, p.IP_ADDRESS, p.ISSUED_AT_UTC })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var activities = rawSessions
            .GroupBy(s => s.USER_ID)
            .Select(g =>
            {
                var ips = g.Select(s => s.IP_ADDRESS)
                    .Where(ip => !string.IsNullOrWhiteSpace(ip))
                    .Select(ip => ip!)
                    .Distinct()
                    .ToList();
                var lastSession = g.OrderByDescending(s => s.ISSUED_AT_UTC).FirstOrDefault();
                return new PlaybackUserActivity(
                    UserId: g.Key,
                    SessionCount: g.Count(),
                    DistinctIpCount: ips.Count,
                    LastIpAddress: lastSession?.IP_ADDRESS,
                    IpAddresses: ips);
            })
            .ToList();

        return activities;
    }

    public void Add(PLAYBACK_SESSION playbackSession) => dbContext.PlaybackSessions().Add(playbackSession);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
