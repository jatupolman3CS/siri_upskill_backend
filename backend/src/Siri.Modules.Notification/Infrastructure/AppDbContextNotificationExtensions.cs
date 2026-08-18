using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessor for the Notification module's entities on the shared
/// <see cref="AppDbContext"/> — same pattern as
/// <c>Siri.Modules.Identity.Infrastructure.AppDbContextIdentityExtensions</c> (see its doc comment
/// for why <see cref="AppDbContext"/> itself carries no <c>DbSet&lt;T&gt;</c> properties).
/// </summary>
public static class AppDbContextNotificationExtensions
{
    public static DbSet<EmailOutboxMessage> EmailOutboxMessages(this AppDbContext context) => context.Set<EmailOutboxMessage>();
}
