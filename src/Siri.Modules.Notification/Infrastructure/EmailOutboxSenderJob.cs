using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Email;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary>
/// The recurring job (registered every minute from Siri.Workers/RecurringJobsRegistration.cs) that
/// drains the outbox over the <see cref="NotificationTransport.Database"/> transport: finds due <see cref="EMAIL_OUTBOX_MESSAGE"/> rows —
/// <see cref="EmailOutboxStatus.Pending"/>, or <see cref="EmailOutboxStatus.Failed"/>-but-retryable with <c>NextRetryAtUtc</c> due —
/// and delivers each through <see cref="EmailDeliveryHandler"/> (the same code the Kafka consumer runs: claim, SMTP via
/// <see cref="IEmailSender"/>, remember, record the outcome through the entity's own domain methods).
/// A send failure is an expected, handled case there (mapped from <see cref="IEmailSender"/>'s
/// <see cref="Result"/>), not something that throws and crashes the job run
/// (backend.md: "ห้ามใช้ exception เป็น control flow").
/// <para>
/// Under the <see cref="NotificationTransport.Kafka"/> transport this job does nothing — the relay and the consumers own delivery.
/// </para>
/// </summary>
public sealed class EmailOutboxSenderJob(
    AppDbContext dbContext,
    EmailDeliveryHandler deliveryHandler,
    IClock clock,
    IOptions<NotificationDeliveryOptions> deliveryOptions,
    ILogger<EmailOutboxSenderJob> logger)
{
    /// <summary>Cap per run so a large backlog can't make one execution run unbounded — whatever is
    /// left over gets picked up by next minute's run.</summary>
    private const int BatchSize = 100;

    /// <summary>
    /// Stops a slow run from overlapping with the next minute's trigger and double-sending the same
    /// batch (Hangfire recurring jobs do not serialize against themselves by default).
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 50)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var options = deliveryOptions.Value;

        if (options.Transport == NotificationTransport.Kafka)
        {
            // The Kafka pipeline owns delivery (relay → topic → consumer). Sending from here too would double-send every email.
            return;
        }

        var now = clock.UtcNow;

        // Rows the Kafka relay handed to a broker (Queued) are normally none under this transport. They exist when an operator
        // switched Kafka off while mail was in flight; once stale (nobody recorded an outcome) this job adopts them so nothing is
        // stranded by the switch.
        var queuedStaleBefore = now - options.QueuedStaleAfter;

        var due = await dbContext.EmailOutboxMessages()
            .Where(m => m.Status == EmailOutboxStatus.Pending
                || (m.Status == EmailOutboxStatus.Failed && m.NextRetryAtUtc != null && m.NextRetryAtUtc <= now)
                || (m.Status == EmailOutboxStatus.Queued && m.QueuedAtUtc != null && m.QueuedAtUtc <= queuedStaleBefore))
            .OrderBy(m => m.Id) // UUIDv7 PK sorts chronologically -> oldest-queued-first, no extra column needed
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (due.Count == 0)
        {
            return;
        }

        var delivered = 0;
        foreach (var message in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Each message is claimed, sent and its outcome saved on its own, so a crash mid-batch can only ever repeat the one message
            // in flight — never the whole batch that was sent but not yet saved.
            var outcome = await deliveryHandler
                .DeliverAsync(message, throttled: false, cancellationToken)
                .ConfigureAwait(false);

            if (outcome == EmailDeliveryOutcome.Delivered)
            {
                delivered++;
            }
        }

        logger.LogInformation("Email outbox run: {Delivered} of {Due} due messages delivered", delivered, due.Count);
    }

    /// <summary>Maps one outbox row onto the message the sender delivers. The iCalendar part (P11-04) is passed
    /// through untouched when the row has one — the <c>METHOD</c> and the document travel together; a row with only
    /// one of the two (impossible through <see cref="EMAIL_OUTBOX_MESSAGE.Enqueue(string, string, string, string?, string?, string?)"/>)
    /// is sent as an ordinary email rather than as a malformed calendar.</summary>
    public static EmailMessage ToEmailMessage(EMAIL_OUTBOX_MESSAGE message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var calendar = message.CalendarMethod is { } method && message.CalendarIcs is { } ics
            ? new EmailCalendarContent(method, ics)
            : null;

        return new EmailMessage(message.ToEmail, message.Subject, message.BodyHtml, calendar);
    }
}
