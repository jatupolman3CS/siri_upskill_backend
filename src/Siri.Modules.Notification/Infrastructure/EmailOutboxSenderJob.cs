using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Integrations.Email;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary>
/// The recurring job (registered every minute from Siri.Workers/RecurringJobsRegistration.cs) that
/// drains the outbox: finds due <see cref="EMAIL_OUTBOX_MESSAGE"/> rows — <see cref="EmailOutboxStatus.Pending"/>,
/// or <see cref="EmailOutboxStatus.Failed"/>-but-retryable with <c>NextRetryAtUtc</c> due — attempts
/// delivery through the registered <see cref="IEmailSender"/>, and records the outcome through the
/// entity's own domain methods (never by mutating EF-tracked properties directly from outside it).
/// A send failure is an expected, handled case here (mapped from <see cref="IEmailSender"/>'s
/// <see cref="Result"/>), not something that throws and crashes the job run
/// (backend.md: "ห้ามใช้ exception เป็น control flow").
/// </summary>
public sealed class EmailOutboxSenderJob(
    AppDbContext dbContext,
    IEmailSender emailSender,
    IClock clock,
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
        var now = clock.UtcNow;

        var due = await dbContext.EmailOutboxMessages()
            .Where(m => m.Status == EmailOutboxStatus.Pending
                || (m.Status == EmailOutboxStatus.Failed && m.NextRetryAtUtc != null && m.NextRetryAtUtc <= now))
            .OrderBy(m => m.Id) // UUIDv7 PK sorts chronologically -> oldest-queued-first, no extra column needed
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (due.Count == 0)
        {
            return;
        }

        foreach (var message in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await emailSender
                .SendAsync(new EmailMessage(message.ToEmail, message.Subject, message.BodyHtml), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                message.RecordSent(clock);
            }
            else
            {
                logger.LogWarning(
                    "Email outbox message {MessageId} to {ToEmail} failed (attempt {Attempt}): {Error}",
                    message.Id,
                    message.ToEmail,
                    message.Attempts + 1,
                    result.Error.Message);

                message.RecordAttemptFailed(result.Error.Message, clock);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
