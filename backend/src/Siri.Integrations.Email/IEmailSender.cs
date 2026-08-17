using Siri.SharedKernel;

namespace Siri.Integrations.Email;

/// <summary>
/// Abstraction over transactional email delivery (enrollment receipts, payout notices, security
/// alerts, ...). The real provider adapter ships in a later phase — this is the interface stub only.
/// </summary>
public interface IEmailSender
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string ToAddress, string Subject, string HtmlBody);
