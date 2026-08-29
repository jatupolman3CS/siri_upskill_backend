using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Application;

public interface IEpisodeAttachmentRepository
{
    Task<EPISODE_ATTACHMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<EPISODE_ATTACHMENT>> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken);

    Task AddAsync(EPISODE_ATTACHMENT attachment, CancellationToken cancellationToken);

    Task DeleteAsync(EPISODE_ATTACHMENT attachment, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
