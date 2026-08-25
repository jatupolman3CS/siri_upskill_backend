using Siri.SharedKernel;

namespace Siri.Integrations.Email;

/// <summary>
/// Abstraction over transactional email delivery (enrollment receipts, payout notices, security
/// alerts, ...). Two implementations exist — <see cref="SmtpEmailSender"/> (real SMTP delivery via
/// MailKit) and <see cref="LoggingEmailSender"/> (no-op fallback) — selected at startup by
/// <see cref="EmailServiceCollectionExtensions.AddEmailIntegration"/>. Callers depend only on this
/// interface, never on a concrete sender.
/// </summary>
public interface IEmailSender
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string ToAddress, string Subject, string HtmlBody);
