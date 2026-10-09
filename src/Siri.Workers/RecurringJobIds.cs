namespace Siri.Workers;

/// <summary>
/// The ids of the platform's recurring jobs — one definition shared by the registration (<see cref="RecurringJobsRegistration.MapRecurringJobs"/>)
/// and by everything that has to recognise them (admin diagnostics). The ids are the job's identity in Hangfire storage: registering the same id
/// from the API host and from a dedicated Workers host rewrites one definition instead of creating a second job, which is what makes running both safe.
/// </summary>
public static class RecurringJobIds
{
    public const string EmailOutboxSend = "email-outbox-send";
    public const string BunnyTranscodePoll = "bunny-transcode-poll";
    public const string AnalyticsNightlyRollup = "analytics-nightly-rollup";
    public const string StripeReconciliation = "stripe-reconciliation";
    public const string PdpaDataRetentionCleanup = "pdpa-data-retention-cleanup";
    public const string PlaybackAnomalyDetection = "playback-anomaly-detection";
    public const string OrderExpiry = "order-expiry";
    public const string AnnouncementDispatch = "announcement-dispatch";
    public const string LiveMeetingSync = "live-meeting-sync";
    public const string LiveInviteReconcile = "live-invite-reconcile";
    public const string LiveSessionReminders = "live-session-reminders";
    public const string CourseEnrollmentRecount = "course-enrollment-recount";
    public const string CourseSearchReindex = "course-search-reindex";

    /// <summary>The jobs the Live system depends on to work end to end, plus the two that feed it (the e-mail outbox that carries every invite and
    /// reminder, and the enrollment recount that keeps the catalog's counters honest) — what the admin Live status reports on.</summary>
    public static readonly IReadOnlyList<string> LiveDiagnostics =
    [
        LiveMeetingSync,
        LiveInviteReconcile,
        LiveSessionReminders,
        EmailOutboxSend,
        CourseEnrollmentRecount,
    ];

    /// <summary>Every recurring job id <see cref="RecurringJobsRegistration.MapRecurringJobs"/> registers.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        EmailOutboxSend,
        BunnyTranscodePoll,
        AnalyticsNightlyRollup,
        StripeReconciliation,
        PdpaDataRetentionCleanup,
        PlaybackAnomalyDetection,
        OrderExpiry,
        AnnouncementDispatch,
        LiveMeetingSync,
        LiveInviteReconcile,
        LiveSessionReminders,
        CourseEnrollmentRecount,
        CourseSearchReindex,
    ];
}
