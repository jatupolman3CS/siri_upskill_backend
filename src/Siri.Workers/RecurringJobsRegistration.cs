using Hangfire;
using Siri.Modules.Analytics.Infrastructure;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Catalog.Infrastructure.Search;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Live.Infrastructure;
using Siri.Modules.Media.Infrastructure;
using Siri.Modules.Notification.Infrastructure;

namespace Siri.Workers;

/// <summary>
/// Single place where the platform's recurring jobs get scheduled (transcode status poll, payout
/// run, email outbox sender, analytics rollup — see ARCHITECTURE.md §2 "Cross-cutting"). Call once
/// at startup, after <see cref="WorkersServiceCollectionExtensions.AddWorkers"/> has wired the
/// Hangfire server. <see cref="IRecurringJobManager.AddOrUpdate"/> is create-or-update, so calling
/// this on every process start is safe/idempotent — and it is called from <b>both</b> hosts that can run the Hangfire server (the dedicated
/// Workers host, and the API when <c>Hangfire:ServerInApi</c> is on), always with the same ids (<see cref="RecurringJobIds"/>), so running both is harmless.
/// </summary>
public static class RecurringJobsRegistration
{
    public static void MapRecurringJobs(this IRecurringJobManager recurringJobManager)
    {
        // 1. Drains notify.EmailOutbox every minute
        recurringJobManager.AddOrUpdate<EmailOutboxSenderJob>(
            RecurringJobIds.EmailOutboxSend,
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely());

        // 2. Polls Bunny Stream upload/transcoding status every 5 minutes
        recurringJobManager.AddOrUpdate<BunnyTranscodePollJob>(
            RecurringJobIds.BunnyTranscodePoll,
            job => job.RunAsync(CancellationToken.None),
            Cron.MinuteInterval(5));

        // 3. Nightly analytics rollup (computes daily course stats & episode drop-offs)
        recurringJobManager.AddOrUpdate<AnalyticsRollupJob>(
            RecurringJobIds.AnalyticsNightlyRollup,
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(2));

        // 4. Daily Stripe payment reconciliation & cleanup
        recurringJobManager.AddOrUpdate<StripeReconciliationJob>(
            RecurringJobIds.StripeReconciliation,
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(3));

        // 5. Nightly PDPA data retention cleanup (P7-04)
        recurringJobManager.AddOrUpdate<DataRetentionCleanupJob>(
            RecurringJobIds.PdpaDataRetentionCleanup,
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(4));

        // 6. Playback anomaly detection job (P2-06: >30 episodes/hr, multi-IP detection)
        recurringJobManager.AddOrUpdate<PlaybackAnomalyDetectionJob>(
            RecurringJobIds.PlaybackAnomalyDetection,
            job => job.RunAsync(CancellationToken.None, PlaybackAnomalyDetectionJob.DefaultMaxSessionsPerHour, PlaybackAnomalyDetectionJob.DefaultMaxDistinctIpsPerHour),
            Cron.MinuteInterval(10));

        // 7. Order expiry job (P3-03: expires stale AwaitingPayment orders every 2 minutes)
        recurringJobManager.AddOrUpdate<OrderExpiryJob>(
            RecurringJobIds.OrderExpiry,
            job => job.RunAsync(CancellationToken.None),
            Cron.MinuteInterval(2));

        // 8. Drains due course announcements every minute (X-31: nothing ever dispatched these before)
        recurringJobManager.AddOrUpdate<AnnouncementDispatchJob>(
            RecurringJobIds.AnnouncementDispatch,
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely());

        // 9. Builds/patches/deletes the online room behind every live session (P11-03: Google Calendar event with a
        // Meet room, or waits for a manually pasted link) and adopts sessions that lack a meeting row
        recurringJobManager.AddOrUpdate<LiveMeetingSyncJob>(
            RecurringJobIds.LiveMeetingSync,
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely());

        // 10. Reconciles who has been told about which upcoming live session (P11-04): purchase-day invite + calendar
        // file, new/moved/cancelled sessions, lost access. Diff-based, so it needs no hook in the payment flow
        recurringJobManager.AddOrUpdate<LiveInviteReconcileJob>(
            RecurringJobIds.LiveInviteReconcile,
            job => job.RunAsync(CancellationToken.None),
            Cron.MinuteInterval(2));

        // 11. 24-hour and 1-hour reminders to invited learners and instructors, and the "room not ready" warning (P11-04)
        recurringJobManager.AddOrUpdate<LiveSessionRemindersJob>(
            RecurringJobIds.LiveSessionReminders,
            job => job.RunAsync(CancellationToken.None),
            Cron.MinuteInterval(5));

        // 11b. Imports a finished class's Google Meet recording as a lesson (P11-13): finds it in Google, copies it Drive -> Bunny, waits for the
        // transcode and attaches it. A no-op until Live:Recording:AutoImport:Enabled; row leases make an overlapping run harmless.
        recurringJobManager.AddOrUpdate<LiveRecordingImportJob>(
            RecurringJobIds.LiveRecordingImport,
            job => job.RunAsync(CancellationToken.None),
            Cron.MinuteInterval(5));

        // 12. Recounts COURSES.ENROLLMENT_COUNT from the enrollments themselves and repairs any course that differs (paged, row-locked per
        // correction): fills in rows that predate the counter's writer and heals drift within the hour. Learning keeps the count current
        // between runs by calling Catalog's ICourseEnrollmentCountUpdater on every enrollment transition.
        recurringJobManager.AddOrUpdate<CourseEnrollmentRecountJob>(
            RecurringJobIds.CourseEnrollmentRecount,
            job => job.RunAsync(CancellationToken.None),
            Cron.Hourly());

        // 13. Reconciles the Meilisearch course index (course text + instructor name) with the database: rewrites every Published course and removes
        // documents of courses no longer Published. Approve/unpublish already sync their own course immediately; this heals whatever those best-effort
        // hooks missed. Offset by 15 minutes from the other hourly job so the two do not start together. A no-op while Meilisearch is not configured.
        recurringJobManager.AddOrUpdate<CourseSearchReindexJob>(
            RecurringJobIds.CourseSearchReindex,
            job => job.RunAsync(CancellationToken.None),
            Cron.Hourly(15));
    }
}
