using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Application;

public interface ICategoryRepository
{
    Task<IReadOnlyList<CATEGORY>> GetAllAsync(CancellationToken cancellationToken);

    Task<CATEGORY?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<CATEGORY?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    Task AddAsync(CATEGORY category, CancellationToken cancellationToken);

    Task UpdateAsync(CATEGORY category, CancellationToken cancellationToken);

    Task DeleteAsync(CATEGORY category, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
