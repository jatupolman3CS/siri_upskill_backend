using Hangfire;
using Siri.Modules.Notification.Infrastructure;

namespace Siri.Workers;

/// <summary>
/// Single place where the platform's recurring jobs get scheduled (transcode status poll, payout
/// run, email outbox sender, analytics rollup — see ARCHITECTURE.md §2 "Cross-cutting"). Call once
/// at startup, after <see cref="WorkersServiceCollectionExtensions.AddWorkers"/> has wired the
/// Hangfire server. <see cref="IRecurringJobManager.AddOrUpdate"/> is create-or-update, so calling
/// this on every process start is safe/idempotent.
/// </summary>
public static class RecurringJobsRegistration
{
    public static void MapRecurringJobs(this IRecurringJobManager recurringJobManager)
    {
        // Drains notify.EmailOutbox every minute — see EmailOutboxSenderJob's own doc comment for
        // the due-message query and retry/backoff policy it drives.
        recurringJobManager.AddOrUpdate<EmailOutboxSenderJob>(
            "email-outbox-send",
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely());
    }
}
