using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Integrations.Video;
using Siri.Modules.Media.Application;
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

        // A completed transfer still needs transcoding. Poll the asset, including those whose
        // upload session was already completed by the browser, until the provider reports a terminal state.
        var assets = await dbContext.Set<MEDIA_ASSET>()
            .Where(a => (a.STATUS == MediaAssetStatus.Processing ||
                (a.STATUS == MediaAssetStatus.Uploading && dbContext.Set<MEDIA_UPLOAD_SESSION>()
                    .Any(s => s.MEDIA_ASSET_ID == a.MEDIA_ASSET_ID && s.STATUS == MediaUploadSessionStatus.Pending))) &&
                a.CreatedAtUtc <= pendingCutoff)
            .OrderBy(a => a.UpdatedAtUtc ?? a.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (assets.Count == 0)
        {
            return;
        }

        var assetIds = assets.Select(a => a.MEDIA_ASSET_ID).ToArray();
        var pendingSessions = await dbContext.Set<MEDIA_UPLOAD_SESSION>()
            .Where(s => assetIds.Contains(s.MEDIA_ASSET_ID) && s.STATUS == MediaUploadSessionStatus.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var sessionsByAsset = pendingSessions.ToLookup(s => s.MEDIA_ASSET_ID);

        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(asset.PROVIDER_ASSET_ID))
            {
                continue;
            }

            var statusResult = await videoProvider
                .GetStatusAsync(asset.PROVIDER_ASSET_ID, cancellationToken)
                .ConfigureAwait(false);

            if (statusResult.IsFailure)
            {
                logger.LogWarning("Could not refresh media asset {AssetId}: {ErrorCode}",
                    asset.MEDIA_ASSET_ID, statusResult.Error.Code);
                continue;
            }

            var status = statusResult.Value;
            MediaAssetStatusUpdater.Apply(asset, status, clock);
            foreach (var session in sessionsByAsset[asset.MEDIA_ASSET_ID])
            {
                if (status.Status is VideoProcessingStatus.Ready or VideoProcessingStatus.Processing)
                {
                    session.Complete();
                }
                else if (status.Status == VideoProcessingStatus.Failed || session.EXPIRES_AT_UTC <= clock.UtcNow)
                {
                    session.MarkExpired();
                }
            }

            logger.LogInformation("Refreshed media asset {AssetId}: {Status}", asset.MEDIA_ASSET_ID, asset.STATUS);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
