using Microsoft.Extensions.Logging;
using Siri.SharedKernel;

namespace Siri.Integrations.Email;

/// <summary>
/// No-op <see cref="IEmailSender"/> that only logs what would have been sent. Registered by default
/// (see <see cref="EmailServiceCollectionExtensions.AddEmailIntegration"/>) whenever no real SMTP
/// host is configured, so the app boots and every email-driven feature (registration, password
/// reset, receipts, ...) is fully runnable and testable in this environment before a production
/// email vendor is chosen (CLAUDE.md's "ยังค้าง" list — no vendor decided yet, unlike Bunny
/// Stream/Stripe/Contabo). Never actually delivers anything.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Email not sent (no SMTP provider configured) — To: {ToAddress}, Subject: {Subject}",
            message.ToAddress,
            message.Subject);

        return Task.FromResult(Result.Success());
    }
}
