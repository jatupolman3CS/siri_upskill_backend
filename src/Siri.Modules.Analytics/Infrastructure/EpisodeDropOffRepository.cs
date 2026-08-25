using Microsoft.EntityFrameworkCore;
using Siri.Modules.Analytics.Application;
using Siri.Modules.Analytics.Domain;
using Siri.Persistence;

namespace Siri.Modules.Analytics.Infrastructure;

/// <summary>Implementation of <see cref="IEpisodeDropOffRepository"/> — see that interface's own doc
/// comment for the full contract.</summary>
public sealed class EpisodeDropOffRepository(AppDbContext dbContext) : IEpisodeDropOffRepository
{
    public async Task<EPISODE_DROP_OFF?> GetAsync(DateOnly date, Guid episodeId, CancellationToken cancellationToken) =>
        await dbContext.EpisodeDropOffs()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.DATE == date && x.EPISODE_ID == episodeId, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<EPISODE_DROP_OFF>> GetForEpisodeAsync(Guid episodeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        await dbContext.EpisodeDropOffs()
            .AsNoTracking()
            .Where(x => x.EPISODE_ID == episodeId && x.DATE >= fromDate && x.DATE <= toDate)
            .OrderBy(x => x.DATE)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
