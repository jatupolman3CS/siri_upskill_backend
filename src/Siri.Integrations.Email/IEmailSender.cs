using Siri.SharedKernel;

namespace Siri.Integrations.Email;

/// <summary>
/// Abstraction over transactional email delivery (enrollment receipts, payout notices, security
/// alerts, ...). <see cref="SmtpEmailSender"/> (real SMTP delivery via MailKit) is the only sender that
/// delivers; <see cref="LoggingEmailSender"/> is an explicit <c>Email:Provider=Log</c> opt-in that sends
/// nothing, and <see cref="UnconfiguredEmailSender"/> fails every send when no provider is set — selected
/// at startup by <see cref="EmailServiceCollectionExtensions.AddEmailIntegration"/>. Callers depend only
/// on this interface, never on a concrete sender.
/// </summary>
public interface IEmailSender
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// An iCalendar document to send as a <c>text/calendar</c> part of an <see cref="EmailMessage"/> (task P11-04).
/// <paramref name="Method"/> is the iTIP method — exactly <c>REQUEST</c>, <c>CANCEL</c> or <c>PUBLISH</c>;
/// <paramref name="IcsContent"/> is the full RFC 5545 document (starts with <c>BEGIN:VCALENDAR</c>, at most
/// <see cref="EmailMimeMessageFactory.MaxCalendarContentLength"/> characters). Both are re-validated by
/// <see cref="EmailMimeMessageFactory"/> before anything is sent.
/// </summary>
public sealed record EmailCalendarContent(string Method, string IcsContent);

/// <summary>
/// One outbound email. <paramref name="Calendar"/> is optional (<c>null</c> = an ordinary HTML email, built
/// byte-for-byte as before); when present the sender emits a multipart message that carries the calendar both as
/// a <c>text/calendar</c> alternative (so mail clients show Accept/Add-to-calendar) and as an <c>invite.ics</c>
/// attachment — see <see cref="EmailMimeMessageFactory"/>.
/// </summary>
public sealed record EmailMessage(string ToAddress, string Subject, string HtmlBody, EmailCalendarContent? Calendar = null);
