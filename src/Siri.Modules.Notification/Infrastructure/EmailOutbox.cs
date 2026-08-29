using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary><see cref="IEmailOutbox"/> implementation — a thin adapter over
/// <see cref="EMAIL_OUTBOX_MESSAGE.Enqueue"/> and this module's own <see cref="AppDbContextNotificationExtensions.EmailOutboxMessages"/>
/// accessor. Scoped: depends on the scoped <see cref="AppDbContext"/>, same lifetime as
/// <see cref="EmailOutboxSenderJob"/>.</summary>
internal sealed class EmailOutbox(AppDbContext dbContext) : IEmailOutbox
{
    public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey)
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue(toEmail, subject, bodyHtml, templateKey);
        dbContext.EmailOutboxMessages().Add(message);
    }
}
