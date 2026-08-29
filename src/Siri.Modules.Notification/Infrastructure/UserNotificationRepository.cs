using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;

namespace Siri.Modules.Notification.Infrastructure;

public sealed class UserNotificationRepository(AppDbContext dbContext) : IUserNotificationRepository
{
    public Task<USER_NOTIFICATION?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.UserNotifications().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<USER_NOTIFICATION> Items, int TotalCount)> GetByUserIdPagedAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var query = dbContext.UserNotifications().Where(n => n.UserId == userId);
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserNotifications().CountAsync(n => n.UserId == userId && n.ReadAtUtc == null, cancellationToken);

    public async Task AddAsync(USER_NOTIFICATION notification, CancellationToken cancellationToken)
    {
        dbContext.UserNotifications().Add(notification);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(USER_NOTIFICATION notification, CancellationToken cancellationToken)
    {
        dbContext.UserNotifications().Update(notification);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
