using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Application;

public interface IInstructorProfileRepository
{
    Task<INSTRUCTOR_PROFILE?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<INSTRUCTOR_PROFILE?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<INSTRUCTOR_PROFILE>> GetPendingApplicationsAsync(CancellationToken cancellationToken);

    Task AddAsync(INSTRUCTOR_PROFILE profile, CancellationToken cancellationToken);

    Task UpdateAsync(INSTRUCTOR_PROFILE profile, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
