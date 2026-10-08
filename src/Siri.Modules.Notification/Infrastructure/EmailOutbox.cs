using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary><see cref="IEmailOutbox"/> implementation — a thin adapter over
/// <see cref="EMAIL_OUTBOX_MESSAGE.Enqueue(string, string, string, string?)"/> and this module's own
/// <see cref="AppDbContextNotificationExtensions.EmailOutboxMessages"/> accessor. Scoped: depends on the scoped
/// <see cref="AppDbContext"/>, same lifetime as <see cref="EmailOutboxSenderJob"/>.
/// Neither overload saves — the caller owns the transaction boundary (see <see cref="IEmailOutbox"/>).</summary>
internal sealed class EmailOutbox(AppDbContext dbContext) : IEmailOutbox
{
    public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey)
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue(toEmail, subject, bodyHtml, templateKey);
        dbContext.EmailOutboxMessages().Add(message);
    }

    /// <summary>Overrides the contract's default body (which rejects calendar parts): the method/ICS pair is
    /// validated by <see cref="EMAIL_OUTBOX_MESSAGE"/> itself — method one of REQUEST/CANCEL/PUBLISH, ICS
    /// non-empty, starts with <c>BEGIN:VCALENDAR</c>, at most 200,000 characters — so a bad part throws here, at the
    /// caller, and nothing is staged.</summary>
    public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey, EmailCalendarPart? calendar)
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue(
            toEmail,
            subject,
            bodyHtml,
            templateKey,
            calendarMethod: calendar?.Method,
            calendarIcs: calendar?.IcsContent);

        dbContext.EmailOutboxMessages().Add(message);
    }
}
