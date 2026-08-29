using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Application;

public interface IUserNotificationRepository
{
    Task<USER_NOTIFICATION?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<USER_NOTIFICATION> Items, int TotalCount)> GetByUserIdPagedAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(USER_NOTIFICATION notification, CancellationToken cancellationToken);

    Task UpdateAsync(USER_NOTIFICATION notification, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
