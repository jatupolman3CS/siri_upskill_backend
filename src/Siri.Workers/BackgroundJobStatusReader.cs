using Hangfire;
using Hangfire.Storage;

namespace Siri.Workers;

/// <summary>One Hangfire processing server currently registered in storage.</summary>
public sealed record BackgroundServerInfo(string Name, DateTime StartedAtUtc, DateTime? HeartbeatUtc);

/// <summary>One recurring job as Hangfire stores it. Times are UTC.</summary>
public sealed record RecurringJobInfo(string Id, string Cron, DateTime? LastExecutionUtc, DateTime? NextExecutionUtc, string? LastJobState);

/// <summary>What a read of Hangfire's storage found: the registered servers and the requested recurring jobs that exist.</summary>
public sealed record BackgroundJobStatus(IReadOnlyList<BackgroundServerInfo> Servers, IReadOnlyList<RecurringJobInfo> RecurringJobs);

/// <summary>
/// Read-only view of the background-job system for operators (admin diagnostics). Reads Hangfire's own monitoring API — it never enqueues, triggers or
/// removes anything. Throws when the storage cannot be read (the caller reports that as a diagnostic, it is not an error of the caller's own).
/// </summary>
public interface IBackgroundJobStatusReader
{
    BackgroundJobStatus GetStatus(IReadOnlyCollection<string> recurringJobIds);
}

/// <summary><see cref="IBackgroundJobStatusReader"/> over the registered <see cref="JobStorage"/>.</summary>
public sealed class HangfireBackgroundJobStatusReader(JobStorage storage) : IBackgroundJobStatusReader
{
    public BackgroundJobStatus GetStatus(IReadOnlyCollection<string> recurringJobIds)
    {
        ArgumentNullException.ThrowIfNull(recurringJobIds);

        var servers = storage.GetMonitoringApi().Servers()
            .Select(server => new BackgroundServerInfo(
                server.Name,
                AsUtc(server.StartedAt) ?? default,
                AsUtc(server.Heartbeat)))
            .OrderBy(server => server.Name, StringComparer.Ordinal)
            .ToArray();

        using var connection = storage.GetConnection();
        var wanted = recurringJobIds.ToHashSet(StringComparer.Ordinal);
        var jobs = connection.GetRecurringJobs()
            .Where(job => wanted.Contains(job.Id))
            .Select(job => new RecurringJobInfo(job.Id, job.Cron, AsUtc(job.LastExecution), AsUtc(job.NextExecution), job.LastJobState))
            .OrderBy(job => job.Id, StringComparer.Ordinal)
            .ToArray();

        return new BackgroundJobStatus(servers, jobs);
    }

    // Hangfire hands back UTC values with an unspecified Kind; make that explicit so they serialize with a Z.
    private static DateTime? AsUtc(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
