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
    public static DbSet<EMAIL_OUTBOX_MESSAGE> EmailOutboxMessages(this AppDbContext context) => context.Set<EMAIL_OUTBOX_MESSAGE>();

    public static DbSet<ANNOUNCEMENT> Announcements(this AppDbContext context) => context.Set<ANNOUNCEMENT>();

    public static DbSet<USER_NOTIFICATION> UserNotifications(this AppDbContext context) => context.Set<USER_NOTIFICATION>();

    public static DbSet<CONTACT_MESSAGE> ContactMessages(this AppDbContext context) => context.Set<CONTACT_MESSAGE>();
}
