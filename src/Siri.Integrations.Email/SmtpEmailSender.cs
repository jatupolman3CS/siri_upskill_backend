using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Siri.SharedKernel;

namespace Siri.Integrations.Email;

/// <summary>
/// Sends email over standard SMTP using MailKit — a well-maintained, modern client library.
/// Deliberately not <c>System.Net.Mail.SmtpClient</c> (the legacy BCL client, which Microsoft no
/// longer recommends for new development) and not hand-rolled SMTP protocol code. Configured purely
/// via <see cref="SmtpEmailSenderOptions"/> (standard host/port/credentials/TLS) so any SMTP-capable
/// provider works by changing configuration only — see that type's doc comment for why.
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpEmailSenderOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        try
        {
            using var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(settings.FromDisplayName, settings.FromAddress));
            mime.To.Add(MailboxAddress.Parse(message.ToAddress));
            mime.Subject = message.Subject;
            mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

            using var client = new SmtpClient();

            var secureSocketOptions = settings.AllowInsecure
                ? SecureSocketOptions.None
                : settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
            await client.ConnectAsync(settings.Host, settings.Port, secureSocketOptions, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }

            await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A send failure (network/auth/mailbox/DNS issue, ...) is an expected, handled outcome
            // here — not something that should propagate and crash the caller (backend.md: "ห้ามใช้
            // exception เป็น control flow"). The outbox sender job maps this Result onto
            // EmailOutboxMessage.RecordAttemptFailed and retries per its backoff policy. A genuine
            // cancellation is deliberately excluded from this catch and left to propagate normally.
            logger.LogWarning(ex, "Failed to send email to {ToAddress}", message.ToAddress);
            return Result.Failure(new DomainError("email.send_failed", $"Failed to send email: {ex.Message}"));
        }
    }
}
