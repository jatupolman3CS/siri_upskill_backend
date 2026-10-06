using Hangfire;
using Siri.Modules.Analytics.Infrastructure;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Media.Infrastructure;
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
        // 1. Drains notify.EmailOutbox every minute
        recurringJobManager.AddOrUpdate<EmailOutboxSenderJob>(
            "email-outbox-send",
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely());

        // 2. Polls Bunny Stream upload/transcoding status every 5 minutes
        recurringJobManager.AddOrUpdate<BunnyTranscodePollJob>(
            "bunny-transcode-poll",
            job => job.RunAsync(CancellationToken.None),
            Cron.MinuteInterval(5));

        // 3. Nightly analytics rollup (computes daily course stats & episode drop-offs)
        recurringJobManager.AddOrUpdate<AnalyticsRollupJob>(
            "analytics-nightly-rollup",
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(2));

        // 4. Daily Stripe payment reconciliation & cleanup
        recurringJobManager.AddOrUpdate<StripeReconciliationJob>(
            "stripe-reconciliation",
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(3));

        // 5. Nightly PDPA data retention cleanup (P7-04)
        recurringJobManager.AddOrUpdate<DataRetentionCleanupJob>(
            "pdpa-data-retention-cleanup",
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(4));

        // 6. Playback anomaly detection job (P2-06: >30 episodes/hr, multi-IP detection)
        recurringJobManager.AddOrUpdate<PlaybackAnomalyDetectionJob>(
            "playback-anomaly-detection",
            job => job.RunAsync(CancellationToken.None, PlaybackAnomalyDetectionJob.DefaultMaxSessionsPerHour, PlaybackAnomalyDetectionJob.DefaultMaxDistinctIpsPerHour),
            Cron.MinuteInterval(10));

        // 7. Order expiry job (P3-03: expires stale AwaitingPayment orders every 2 minutes)
        recurringJobManager.AddOrUpdate<OrderExpiryJob>(
            "order-expiry",
            job => job.RunAsync(CancellationToken.None),
            Cron.MinuteInterval(2));

        // 8. Drains due course announcements every minute (X-31: nothing ever dispatched these before)
        recurringJobManager.AddOrUpdate<AnnouncementDispatchJob>(
            "announcement-dispatch",
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely());
    }
}
