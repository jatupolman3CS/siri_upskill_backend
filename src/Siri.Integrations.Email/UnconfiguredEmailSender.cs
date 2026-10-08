using Microsoft.Extensions.Logging;
using Siri.SharedKernel;

namespace Siri.Integrations.Email;

/// <summary>
/// <see cref="IEmailSender"/> registered when <c>Email:Provider</c> is not set at all. It never
/// pretends to send: every call fails with <see cref="ProviderNotConfiguredCode"/> so the email outbox
/// records the failure (and retries once an operator configures SMTP) instead of marking mail as sent.
/// Choose <c>Smtp</c> for real delivery, or <c>Log</c> to explicitly opt in to delivering nothing.
/// </summary>
public sealed class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender
{
    /// <summary>Ends in the shared <c>_not_configured</c> suffix, so it maps to HTTP 503 if it ever
    /// surfaces through an API.</summary>
    public const string ProviderNotConfiguredCode = "email.provider_not_configured";

    public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogError(
            "Email NOT sent: 'Email:Provider' is not configured (set it to 'Smtp'). To: {ToAddress}, Subject: {Subject}",
            message.ToAddress,
            message.Subject);

        return Task.FromResult(Result.Failure(new DomainError(
            ProviderNotConfiguredCode,
            "Email provider is not configured (set Email:Provider to Smtp).")));
    }
}
