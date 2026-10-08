using Microsoft.Extensions.Logging;
using Siri.SharedKernel;

namespace Siri.Integrations.Email;

/// <summary>
/// No-op <see cref="IEmailSender"/> that only logs what would have been sent. Selected ONLY by the
/// explicit opt-in <c>Email:Provider=Log</c> (see
/// <see cref="EmailServiceCollectionExtensions.AddEmailIntegration"/>) — used by the integration
/// tests so they never send real mail. It is never the default, and
/// <c>ProductionConfigurationGuard</c> refuses to start Production with it. It reports success so
/// callers keep working, but delivers nothing, so each call is logged as a warning.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Email NOT delivered ('Email:Provider' is 'Log', an explicit opt-in that sends nothing) — To: {ToAddress}, Subject: {Subject}",
            message.ToAddress,
            message.Subject);

        return Task.FromResult(Result.Success());
    }
}
