using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Integrations.Video;
using Siri.Modules.Media.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Infrastructure;

/// <summary>
/// Periodic Hangfire recurring job that polls for pending Bunny Stream transcode sessions
/// to ensure status is updated even if a webhook was dropped or delayed.
/// </summary>
public sealed class BunnyTranscodePollJob(
    AppDbContext dbContext,
    IVideoProvider videoProvider,
    IClock clock,
    ILogger<BunnyTranscodePollJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var pendingCutoff = clock.UtcNow.AddMinutes(-2);

        var pendingSessions = await dbContext.Set<MEDIA_UPLOAD_SESSION>()
            .Where(s => s.STATUS == MediaUploadSessionStatus.Pending && s.CreatedAtUtc <= pendingCutoff)
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pendingSessions.Count == 0)
        {
            return;
        }

        foreach (var session in pendingSessions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var asset = await dbContext.Set<MEDIA_ASSET>()
                .FirstOrDefaultAsync(a => a.MEDIA_ASSET_ID == session.MEDIA_ASSET_ID, cancellationToken)
                .ConfigureAwait(false);

            if (asset is null || string.IsNullOrWhiteSpace(asset.PROVIDER_ASSET_ID))
            {
                continue;
            }

            var statusResult = await videoProvider
                .GetStatusAsync(asset.PROVIDER_ASSET_ID, cancellationToken)
                .ConfigureAwait(false);

            if (statusResult.IsSuccess)
            {
                var status = statusResult.Value;
                if (status.Status == VideoProcessingStatus.Ready)
                {
                    session.Complete();
                    var durationSec = (int)(status.Duration?.TotalSeconds ?? 0);
                    asset.MarkReady(asset.PROVIDER_ASSET_ID, durationSec, null, clock);
                    logger.LogInformation("Marked media upload session {SessionId} as Ready", session.MEDIA_UPLOAD_SESSION_ID);
                }
                else if (status.Status == VideoProcessingStatus.Failed)
                {
                    session.MarkExpired();
                    asset.MarkFailed("Transcoding failed on video provider");
                    logger.LogWarning("Marked media upload session {SessionId} as Failed", session.MEDIA_UPLOAD_SESSION_ID);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
