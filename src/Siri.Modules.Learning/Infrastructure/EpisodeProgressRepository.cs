using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure;

public sealed class EpisodeProgressRepository(AppDbContext dbContext) : IEpisodeProgressRepository
{
    public Task<EPISODE_PROGRESS?> GetByEnrollmentAndEpisodeAsync(Guid enrollmentId, Guid episodeId, CancellationToken cancellationToken) =>
        dbContext.EpisodeProgresses().FirstOrDefaultAsync(p => p.ENROLLMENT_ID == enrollmentId && p.EPISODE_ID == episodeId, cancellationToken);

    public async Task<IReadOnlyList<EPISODE_PROGRESS>> ListForEnrollmentAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
        await dbContext.EpisodeProgresses()
            .AsNoTracking()
            .Where(p => p.ENROLLMENT_ID == enrollmentId)
            .OrderBy(p => p.UPDATED_AT_UTC)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(EPISODE_PROGRESS episodeProgress) => dbContext.EpisodeProgresses().Add(episodeProgress);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
