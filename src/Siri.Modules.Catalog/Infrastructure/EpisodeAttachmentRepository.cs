using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class EpisodeAttachmentRepository(AppDbContext dbContext) : IEpisodeAttachmentRepository
{
    public Task<EPISODE_ATTACHMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.EpisodeAttachments().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EPISODE_ATTACHMENT>> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken) =>
        await dbContext.EpisodeAttachments()
            .Where(a => a.EpisodeId == episodeId)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(EPISODE_ATTACHMENT attachment, CancellationToken cancellationToken)
    {
        dbContext.EpisodeAttachments().Add(attachment);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(EPISODE_ATTACHMENT attachment, CancellationToken cancellationToken)
    {
        dbContext.EpisodeAttachments().Remove(attachment);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
