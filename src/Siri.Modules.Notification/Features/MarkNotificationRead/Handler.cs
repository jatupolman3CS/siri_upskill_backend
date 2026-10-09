using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Features.MarkNotificationRead;

public sealed class MarkNotificationReadHandler(AppDbContext dbContext, IClock clock, IUnreadNotificationCounter unreadCounter)
{
    public async Task<Result> HandleAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var notification = await dbContext.UserNotifications()
            .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken)
            .ConfigureAwait(false);

        if (notification is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบการแจ้งเตือนที่ระบุ"));
        }

        if (notification.UserId != userId)
        {
            return Result.Failure(DomainError.Forbidden("คุณไม่มีสิทธิ์เข้าถึงการแจ้งเตือนนี้"));
        }

        notification.MarkRead(clock);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The badge must drop right away, not after the cached count expires.
        await unreadCounter.InvalidateAsync(userId, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
