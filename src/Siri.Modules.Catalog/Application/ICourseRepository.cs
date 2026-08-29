using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Application;

public interface ICourseRepository
{
    Task<COURSE?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<COURSE?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    Task<COURSE?> GetWithDetailsByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<COURSE?> GetWithDetailsBySlugAsync(string slug, CancellationToken cancellationToken);

    Task<COURSE?> GetBuilderByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<COURSE>> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken);

    Task<IReadOnlyList<COURSE>> GetPendingModerationAsync(CancellationToken cancellationToken);

    Task AddAsync(COURSE course, CancellationToken cancellationToken);

    Task UpdateAsync(COURSE course, CancellationToken cancellationToken);

    Task DeleteAsync(COURSE course, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken);
}
