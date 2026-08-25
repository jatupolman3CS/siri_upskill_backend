using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Features.GetMyNotifications;

public sealed record NotificationResponse(
    Guid Id,
    Guid UserId,
    string Type,
    string Title,
    string Body,
    string? LinkUrl,
    DateTime? ReadAtUtc,
    DateTime CreatedAtUtc);

public sealed class GetMyNotificationsHandler(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<NotificationResponse>> HandleAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var notifications = await dbContext.UserNotifications()
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(50)
            .Select(n => new NotificationResponse(
                n.Id,
                n.UserId,
                n.Type,
                n.Title,
                n.Body,
                n.LinkUrl,
                n.ReadAtUtc,
                n.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return notifications;
    }
}
