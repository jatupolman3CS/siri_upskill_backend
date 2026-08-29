using FluentValidation;
using Microsoft.Extensions.Logging;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Features.SubmitContactMessage;

public sealed class SubmitContactMessageHandler(
    AppDbContext dbContext,
    IValidator<SubmitContactMessageCommand> validator,
    ILogger<SubmitContactMessageHandler> logger)
{
    public async Task<Result<SubmitContactMessageResponse>> HandleAsync(
        SubmitContactMessageCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Honeypot check: If the hidden bot field is filled, silently return success without saving
        if (!string.IsNullOrWhiteSpace(command.BotField))
        {
            logger.LogInformation("Contact message dropped due to bot honeypot trigger.");
            return Result.Success(new SubmitContactMessageResponse(true, "Your message has been sent successfully."));
        }

        // 2. Validation
        var validationResult = await validator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validationResult.IsValid)
        {
            var errorMessage = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            return Result.Failure<SubmitContactMessageResponse>(
                DomainError.Validation(errorMessage));
        }

        // 3. Create entity
        var contactMessage = new CONTACT_MESSAGE(
            UuidV7.NewId(),
            command.Name,
            command.Email,
            command.Subject,
            command.Message);

        dbContext.ContactMessages().Add(contactMessage);

        // 4. Enqueue notification email to team via EmailOutbox
        var safeSubject = contactMessage.Subject;
        var bodyHtml = $@"<div style=""font-family: sans-serif; line-height: 1.6;"">
<h2>New Contact Message from SiriUpSkill</h2>
<p><strong>From:</strong> {System.Net.WebUtility.HtmlEncode(contactMessage.Name)} &lt;{System.Net.WebUtility.HtmlEncode(contactMessage.Email)}&gt;</p>
<p><strong>Subject:</strong> {System.Net.WebUtility.HtmlEncode(contactMessage.Subject)}</p>
<div style=""background: #f4f4f5; padding: 16px; border-radius: 8px; margin: 16px 0;"">
{System.Net.WebUtility.HtmlEncode(contactMessage.Message).Replace("\n", "<br/>")}
</div>
<p style=""color: #71717a; font-size: 12px;"">Message ID: {contactMessage.Id}</p>
</div>";

        var outboxMessage = EMAIL_OUTBOX_MESSAGE.Enqueue(
            toEmail: "support@siriupskill.com",
            subject: $"[Contact Form] {safeSubject}",
            bodyHtml: bodyHtml,
            templateKey: "contact-notification");

        dbContext.EmailOutboxMessages().Add(outboxMessage);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Log without PII
        logger.LogInformation("Contact message received with Id {MessageId}", contactMessage.Id);

        return Result.Success(new SubmitContactMessageResponse(true, "Your message has been sent successfully."));
    }
}
