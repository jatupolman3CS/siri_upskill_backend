using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Application;

public interface IAnnouncementRepository
{
    Task<ANNOUNCEMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ANNOUNCEMENT>> GetByCourseIdAsync(Guid courseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ANNOUNCEMENT>> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken);

    Task AddAsync(ANNOUNCEMENT announcement, CancellationToken cancellationToken);

    Task UpdateAsync(ANNOUNCEMENT announcement, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
