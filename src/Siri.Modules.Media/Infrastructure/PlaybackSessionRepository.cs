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

    public void Add(PLAYBACK_SESSION playbackSession) => dbContext.PlaybackSessions().Add(playbackSession);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
