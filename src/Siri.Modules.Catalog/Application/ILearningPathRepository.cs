using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Application;

public interface ILearningPathRepository
{
    Task<IReadOnlyList<LEARNING_PATH>> GetAllAsync(bool activeOnly, CancellationToken cancellationToken);

    Task<LEARNING_PATH?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<LEARNING_PATH?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    Task<LEARNING_PATH?> GetWithItemsByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<LEARNING_PATH?> GetWithItemsBySlugAsync(string slug, CancellationToken cancellationToken);

    Task AddAsync(LEARNING_PATH learningPath, CancellationToken cancellationToken);

    Task UpdateAsync(LEARNING_PATH learningPath, CancellationToken cancellationToken);

    Task DeleteAsync(LEARNING_PATH learningPath, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
