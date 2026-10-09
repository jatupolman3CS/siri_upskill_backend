using Microsoft.Extensions.Logging;
using Siri.Integrations.Email;
using Siri.Integrations.Messaging;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Infrastructure.Delivery;

/// <summary>What happened to one email-delivery record.</summary>
public enum EmailDeliveryOutcome
{
    /// <summary>Sent over SMTP and recorded.</summary>
    Delivered,

    /// <summary>The send failed; the outbox row holds the error and the time of the next attempt.</summary>
    FailedWillRetry,

    /// <summary>The send failed and no attempts remain; the row is a dead letter.</summary>
    FailedExhausted,

    /// <summary>Nothing to do: the row was already sent, is exhausted, or no longer exists.</summary>
    Skipped,

    /// <summary>Another consumer is delivering it right now; it will record the outcome (or its claim lapses and the relay republishes).</summary>
    InFlightElsewhere,

    /// <summary>Redis knew an earlier run had sent it; the row was brought up to date without sending again.</summary>
    RecordedEarlierDelivery,
}

/// <summary>
/// Consumes one email-delivery record: loads the outbox row it points at, and — under a Redis claim, within the send budget —
/// sends it over SMTP and records the outcome in the row. Idempotent, because the pipeline is at-least-once.
/// <para>
/// Business failures (SMTP refused) are <i>results</i>, not exceptions: they are written to the row and Kafka moves on; the relay
/// republishes the row when its retry time comes. Only infrastructure failures (database/Redis down) throw — and the consumer
/// then retries the record in place.
/// </para>
/// </summary>
public sealed class EmailDeliveryHandler(
    IEmailOutboxRepository repository,
    IEmailSender emailSender,
    IEmailDeliveryGuard guard,
    IEmailSendThrottle throttle,
    IClock clock,
    ILogger<EmailDeliveryHandler> logger,
    IEmailConsumerHeartbeat? heartbeat = null) : IMessageHandler
{
    public Task HandleAsync(ConsumedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return ProcessAsync(EmailDeliveryMessage.Parse(message.Value).MessageId, cancellationToken);
    }

    /// <summary>Loads the row a delivery record points at and delivers it. See <see cref="DeliverAsync"/>.</summary>
    public async Task<EmailDeliveryOutcome> ProcessAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var row = await repository.GetByIdAsync(messageId, cancellationToken).ConfigureAwait(false);

        var outcome = row is null
            ? Skip("missing", messageId)
            : await DeliverAsync(row, throttled: true, cancellationToken).ConfigureAwait(false);

        // Any record that was dealt with — delivered, refused, skipped — proves the consumers are alive and working through the topic. (A record that
        // threw is not progress.) The relay reads this before re-claiming rows that merely wait in Kafka behind a long backlog. Only present when
        // the Kafka pipeline is registered; the polling transport has no consumers to report on.
        if (heartbeat is not null)
        {
            await heartbeat.BeatAsync(cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }

    /// <summary>
    /// Delivers one outbox row that is tracked by the repository's context, and records the outcome (saving it). The single place that
    /// knows the safe order of "claim → send → remember in Redis → record in the database": the Kafka consumer and the database-polling
    /// sender job both go through it, so the two transports share one claim and can never both send the same mail — even while a
    /// rolling deploy or a configuration skew has them running side by side.
    /// </summary>
    /// <param name="row">An outbox row loaded (tracked) through the same scope's <see cref="IEmailOutboxRepository"/>.</param>
    /// <param name="throttled">Whether to wait for the cluster-wide per-minute send budget first. The polling job passes <c>false</c>:
    /// it is already bounded to one batch a minute, and waiting inside a Hangfire job would run into its concurrency lock.</param>
    public async Task<EmailDeliveryOutcome> DeliverAsync(EMAIL_OUTBOX_MESSAGE row, bool throttled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        var messageId = row.Id;

        if (row.Status == EmailOutboxStatus.Sent)
        {
            return Skip("already_sent", messageId);
        }

        if (row.IsExhausted)
        {
            return Skip("exhausted", messageId);
        }

        var ticket = await guard.TryClaimAsync(messageId, cancellationToken).ConfigureAwait(false);
        switch (ticket.Outcome)
        {
            case DeliveryClaim.AlreadyDelivered:
                // A previous run got the mail out but died (or failed to save) before the row said so. Say so now — sending again would duplicate it.
                row.RecordSent(clock);
                await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                NotificationTelemetry.EmailSkipped.Add(1, new KeyValuePair<string, object?>("reason", "already_delivered"));
                logger.LogInformation("Email {MessageId} was already delivered by an earlier run; row updated", messageId);
                return EmailDeliveryOutcome.RecordedEarlierDelivery;

            case DeliveryClaim.InFlightElsewhere:
                NotificationTelemetry.EmailSkipped.Add(1, new KeyValuePair<string, object?>("reason", "in_flight"));
                return EmailDeliveryOutcome.InFlightElsewhere;
        }

        var delivered = false;
        try
        {
            if (throttled)
            {
                await throttle.WaitForSlotAsync(cancellationToken).ConfigureAwait(false);
            }

            var result = await emailSender
                .SendAsync(EmailOutboxSenderJob.ToEmailMessage(row), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                // SMTP has the mail. Remember that before anything else can fail, so a retry can never send it a second time.
                await guard.MarkDeliveredAsync(messageId, cancellationToken).ConfigureAwait(false);
                delivered = true;

                row.RecordSent(clock);
                await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                NotificationTelemetry.EmailDelivered.Add(1);
                return EmailDeliveryOutcome.Delivered;
            }

            row.RecordAttemptFailed(result.Error.Message, clock);
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (row.IsExhausted)
            {
                NotificationTelemetry.EmailExhausted.Add(1);
                logger.LogError(
                    "Email {MessageId} (template {TemplateKey}) failed its last attempt ({Attempts}) and will not be retried: {Error}",
                    messageId,
                    row.TemplateKey,
                    row.Attempts,
                    result.Error.Message);
                return EmailDeliveryOutcome.FailedExhausted;
            }

            NotificationTelemetry.EmailFailed.Add(1);
            logger.LogWarning(
                "Email {MessageId} failed (attempt {Attempt}); retry due {NextRetryAtUtc:o}: {Error}",
                messageId,
                row.Attempts,
                row.NextRetryAtUtc,
                result.Error.Message);
            return EmailDeliveryOutcome.FailedWillRetry;
        }
        finally
        {
            if (!delivered)
            {
                // Failed or threw: free the claim so the retry (or another instance) is not locked out for the rest of its lifetime.
                await guard.ReleaseAsync(messageId, ticket.Token, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private EmailDeliveryOutcome Skip(string reason, Guid messageId)
    {
        NotificationTelemetry.EmailSkipped.Add(1, new KeyValuePair<string, object?>("reason", reason));
        logger.LogInformation("Email delivery record for {MessageId} skipped: {Reason}", messageId, reason);
        return EmailDeliveryOutcome.Skipped;
    }
}
