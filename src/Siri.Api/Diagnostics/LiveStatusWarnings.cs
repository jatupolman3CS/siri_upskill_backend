namespace Siri.Api.Diagnostics;

/// <summary>Everything the warning rules look at — plain values, so the rules are a pure function that tests can drive directly.</summary>
/// <param name="JobStorageReadable">Hangfire's storage could be read at all. When <c>false</c> nothing about the job server or recurring jobs is known.</param>
/// <param name="JobServerRunning">At least one server has a fresh heartbeat.</param>
/// <param name="RecurringJobs">The reported recurring jobs that exist in storage (a missing one is simply absent).</param>
/// <param name="ExpectedRecurringJobIds">The ids that should exist when a server has scheduled the jobs.</param>
/// <param name="EmailStatusReadable">The outbox counts could be read.</param>
/// <param name="LiveStatusReadable">The Live counts could be read.</param>
/// <param name="PublicBaseUrl">The configured public origin <b>as configured</b> (the response only ever shows its host; the rules need the scheme).</param>
/// <param name="StuckPendingMeetings">Pending rooms that are due and untouched for too long (not part of the response, only of the rules).</param>
public sealed record LiveStatusFacts(
    DateTime NowUtc,
    bool JobStorageReadable,
    bool JobServerRunning,
    IReadOnlyList<RecurringJobStatusDto> RecurringJobs,
    IReadOnlyCollection<string> ExpectedRecurringJobIds,
    string EmailProvider,
    bool EmailStatusReadable,
    int EmailPendingCount,
    int EmailFailedCount,
    long? EmailOldestPendingAgeSeconds,
    string LiveProvider,
    bool GoogleConfigured,
    string PublicBaseUrl,
    bool LiveStatusReadable,
    int MeetingsFailed,
    int StuckPendingMeetings);

/// <summary>
/// The rules that turn raw status into operator warnings — stable <c>snake_case</c> codes (the admin UI and the one-line startup log both show them, and
/// people grep for them), never prose and never a value. Order is fixed (the order of <see cref="Evaluate"/>) so the output is deterministic.
/// </summary>
public static class LiveStatusWarnings
{
    // ---- Codes ---------------------------------------------------------------------------------------

    /// <summary>No Hangfire server has a fresh heartbeat: no background job runs — rooms stay pending, no invite / reminder / confirmation e-mail is ever sent.</summary>
    public const string NoJobServer = "no_job_server";

    /// <summary>Hangfire's storage could not be read, so job state is unknown.</summary>
    public const string JobStorageUnreadable = "job_storage_unreadable";

    /// <summary>One or more expected recurring jobs are not scheduled (no host has registered them).</summary>
    public const string RecurringJobsMissing = "recurring_jobs_missing";

    /// <summary>A scheduled recurring job is long past its next run although a server is up.</summary>
    public const string RecurringJobOverdue = "recurring_job_overdue";

    /// <summary>The last run of a recurring job failed.</summary>
    public const string RecurringJobFailing = "recurring_job_failing";

    /// <summary><c>Email:Provider</c> is not <c>Smtp</c>: mail is dropped (<c>Log</c>) or fails and piles up (<c>Unconfigured</c>).</summary>
    public const string EmailUnconfigured = "email_unconfigured";

    /// <summary>The outbox is not draining: too many waiting messages, or the oldest has waited too long.</summary>
    public const string OutboxBacklog = "outbox_backlog";

    /// <summary>Some messages exhausted every retry and will never be sent.</summary>
    public const string OutboxFailures = "outbox_failures";

    /// <summary>The outbox counts could not be read.</summary>
    public const string EmailStatusUnreadable = "email_status_unreadable";

    /// <summary>The public site URL is not https: links in invitations and the Google redirect would be insecure or wrong.</summary>
    public const string PublicBaseUrlNotHttps = "public_base_url_not_https";

    /// <summary>The provider is <c>GoogleMeet</c> but no Google OAuth client is configured: rooms are manual links only.</summary>
    public const string GoogleNotConfigured = "google_not_configured";

    /// <summary>The development fake provider is active.</summary>
    public const string LiveProviderLogging = "live_provider_logging";

    /// <summary>Rooms are due for processing but untouched for far longer than the job's one-minute cadence.</summary>
    public const string MeetingsStuckPending = "meetings_stuck_pending";

    /// <summary>Some rooms failed after every retry and need the instructor (or an operator) to act.</summary>
    public const string MeetingsFailed = "meetings_failed";

    /// <summary>The Live counts could not be read.</summary>
    public const string LiveStatusUnreadable = "live_status_unreadable";

    // ---- Thresholds ----------------------------------------------------------------------------------

    /// <summary>A server whose last heartbeat is older than this is not counted as running (Hangfire beats every 30 seconds).</summary>
    public static readonly TimeSpan ServerHeartbeatStaleAfter = TimeSpan.FromMinutes(2);

    /// <summary>A recurring job whose next run is this far in the past is overdue.</summary>
    public static readonly TimeSpan RecurringJobOverdueAfter = TimeSpan.FromMinutes(5);

    /// <summary>The sender runs every minute, so a message waiting this long means it is not draining.</summary>
    public static readonly TimeSpan OutboxOldestPendingLimit = TimeSpan.FromMinutes(10);

    /// <summary>This many waiting messages is a backlog regardless of age.</summary>
    public const int OutboxPendingLimit = 100;

    public static IReadOnlyList<string> Evaluate(LiveStatusFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var warnings = new List<string>();

        // ---- Background jobs ----
        if (!facts.JobStorageReadable)
        {
            warnings.Add(JobStorageUnreadable);
        }

        if (!facts.JobServerRunning)
        {
            warnings.Add(NoJobServer);
        }

        if (facts.JobStorageReadable)
        {
            var present = facts.RecurringJobs.Select(job => job.Id).ToHashSet(StringComparer.Ordinal);
            if (facts.ExpectedRecurringJobIds.Any(id => !present.Contains(id)))
            {
                warnings.Add(RecurringJobsMissing);
            }

            // Overdue only means something while a server is up (with none, no_job_server already says it).
            if (facts.JobServerRunning
                && facts.RecurringJobs.Any(job => job.NextExecutionUtc is { } next && next < facts.NowUtc - RecurringJobOverdueAfter))
            {
                warnings.Add(RecurringJobOverdue);
            }

            if (facts.RecurringJobs.Any(job => string.Equals(job.LastJobState, "Failed", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(RecurringJobFailing);
            }
        }

        // ---- E-mail ----
        if (!string.Equals(facts.EmailProvider, "Smtp", StringComparison.Ordinal))
        {
            warnings.Add(EmailUnconfigured);
        }

        if (!facts.EmailStatusReadable)
        {
            warnings.Add(EmailStatusUnreadable);
        }
        else
        {
            if (facts.EmailPendingCount >= OutboxPendingLimit
                || (facts.EmailOldestPendingAgeSeconds is { } age && age > OutboxOldestPendingLimit.TotalSeconds))
            {
                warnings.Add(OutboxBacklog);
            }

            if (facts.EmailFailedCount > 0)
            {
                warnings.Add(OutboxFailures);
            }
        }

        // ---- Live ----
        if (!facts.PublicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(PublicBaseUrlNotHttps);
        }

        if (string.Equals(facts.LiveProvider, "GoogleMeet", StringComparison.Ordinal) && !facts.GoogleConfigured)
        {
            warnings.Add(GoogleNotConfigured);
        }

        if (string.Equals(facts.LiveProvider, "Logging", StringComparison.Ordinal))
        {
            warnings.Add(LiveProviderLogging);
        }

        if (!facts.LiveStatusReadable)
        {
            warnings.Add(LiveStatusUnreadable);
        }
        else
        {
            if (facts.StuckPendingMeetings > 0)
            {
                warnings.Add(MeetingsStuckPending);
            }

            if (facts.MeetingsFailed > 0)
            {
                warnings.Add(MeetingsFailed);
            }
        }

        return warnings;
    }
}
