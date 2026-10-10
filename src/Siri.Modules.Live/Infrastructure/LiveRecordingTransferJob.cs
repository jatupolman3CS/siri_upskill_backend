using Hangfire;
using Hangfire.Common;
using Hangfire.Server;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// The background job (one per claimed import, queued by the <c>live-recording-import</c> tick through <see cref="Application.IRecordingTransferScheduler"/>) that copies
/// a Google Meet recording from Drive to the video provider (P11-13). It exists so the long copy of a multi-GB file never runs inside the five-minute recurring tick:
/// the tick would otherwise hold its storage lock for hours, every following tick would wait for it, fail, and show as a failing recurring job.
/// <para>
/// All the logic lives in <see cref="LiveRecordingImportJob.RunTransferAsync"/>; this class is the Hangfire entry point and carries the two attributes that make it safe:
/// <b>no automatic retry</b> (the import row's state machine owns retries - a failed copy goes back to <c>Waiting</c> with an attempt counted and a backoff) and a
/// <b>per-import</b> storage lock (<see cref="DisableConcurrentTransferPerImportAttribute"/>), so two jobs for the same import - possible only when a copy outlived its
/// lease and the tick reclaimed the row - can never copy the same recording at the same time. The loser finds the row already moved on and does nothing.
/// </para>
/// </summary>
public sealed class LiveRecordingTransferJob(LiveRecordingImportJob importJob)
{
    /// <summary>How long a second job for the same import waits for the first to finish before giving up (it would only ever find the row already moved on).</summary>
    public const int LockTimeoutSeconds = 900;

    [DisableConcurrentTransferPerImport(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid importId, CancellationToken cancellationToken) =>
        importJob.RunTransferAsync(importId, cancellationToken);
}

/// <summary>
/// <see cref="DisableConcurrentExecutionAttribute"/> keyed by the job's <b>first argument</b> (the import id) instead of by method: transfers of different imports run side by
/// side, two of the same import never do. The lock is Hangfire's storage-backed distributed lock, so it also holds across servers (API + Workers).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableConcurrentTransferPerImportAttribute(int timeoutSeconds) : JobFilterAttribute, IServerFilter
{
    private const string LockItemKey = "LiveRecordingTransferLock";

    /// <summary>The lock name for the job: one per import id.</summary>
    public static string ResourceFor(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var importId = job.Args.Count > 0 && job.Args[0] is Guid id ? id : Guid.Empty;
        return $"live-recording-transfer:{importId:N}";
    }

    public void OnPerforming(PerformingContext filterContext)
    {
        var resource = ResourceFor(filterContext.BackgroundJob.Job);
        filterContext.Items[LockItemKey] = filterContext.Connection.AcquireDistributedLock(resource, TimeSpan.FromSeconds(timeoutSeconds));
    }

    public void OnPerformed(PerformedContext filterContext)
    {
        if (filterContext.Items.TryGetValue(LockItemKey, out var held) && held is IDisposable distributedLock)
        {
            distributedLock.Dispose();
        }
    }
}
