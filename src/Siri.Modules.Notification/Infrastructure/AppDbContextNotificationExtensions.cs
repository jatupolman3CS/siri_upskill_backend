using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessor for the Notification module's entities on the shared
/// <see cref="AppDbContext"/>.
/// </summary>
public static class AppDbContextNotificationExtensions
{
    public static DbSet<EmailOutboxMessage> EmailOutboxMessages(this AppDbContext context) => context.Set<EmailOutboxMessage>();

    public static DbSet<Announcement> Announcements(this AppDbContext context) => context.Set<Announcement>();

    public static DbSet<UserNotification> UserNotifications(this AppDbContext context) => context.Set<UserNotification>();
}
