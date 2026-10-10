using Hangfire;
using Siri.Modules.Live.Application;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// <see cref="IRecordingTransferScheduler"/> over Hangfire: one fire-and-forget background job per claimed import, run by whichever Hangfire server picks it up
/// (the dedicated Workers host, or the API's own server when <c>Hangfire:ServerInApi</c> is on - both register <see cref="LiveRecordingTransferJob"/> through
/// <c>AddLiveModule</c>). The job carries only the import's id.
/// </summary>
public sealed class HangfireRecordingTransferScheduler(IBackgroundJobClient jobs) : IRecordingTransferScheduler
{
    public void Enqueue(Guid importId)
    {
        if (importId == Guid.Empty)
        {
            throw new ArgumentException("Import ID cannot be empty.", nameof(importId));
        }

        jobs.Enqueue<LiveRecordingTransferJob>(job => job.RunAsync(importId, CancellationToken.None));
    }
}
