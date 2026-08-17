using Hangfire;

namespace Siri.Workers;

/// <summary>
/// Single place where the platform's recurring jobs get scheduled (transcode status poll, payout
/// run, email digest, analytics rollup — see ARCHITECTURE.md §2 "Cross-cutting"). Call once at
/// startup, after <see cref="WorkersServiceCollectionExtensions.AddWorkers"/> has wired the
/// Hangfire server. Placeholder only — no real jobs exist yet; each job gets added here once the
/// owning module's feature work lands.
/// </summary>
public static class RecurringJobsRegistration
{
    public static void MapRecurringJobs(this IRecurringJobManager recurringJobManager)
    {
        // Intentionally empty for now. Example of the shape a future registration will take:
        //   recurringJobManager.AddOrUpdate<TranscodeStatusPollJob>(
        //       "transcode-status-poll", job => job.RunAsync(CancellationToken.None), Cron.Minutely());
    }
}
