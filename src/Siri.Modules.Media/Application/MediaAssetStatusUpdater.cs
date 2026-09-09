using Siri.Integrations.Video;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

internal static class MediaAssetStatusUpdater
{
    public static bool Apply(MEDIA_ASSET asset, VideoStatus status, IClock clock)
    {
        if (asset.STATUS is not (MediaAssetStatus.Uploading or MediaAssetStatus.Processing))
        {
            return false;
        }

        switch (status.Status)
        {
            case VideoProcessingStatus.Ready:
                asset.MarkReady(asset.PROVIDER_ASSET_ID,
                    Math.Max(0, (int)Math.Ceiling(status.Duration?.TotalSeconds ?? 0)), asset.THUMBNAIL_URL, clock);
                return true;
            case VideoProcessingStatus.Failed:
                asset.MarkFailed("Transcoding failed on video provider.");
                return true;
            case VideoProcessingStatus.Processing when asset.STATUS == MediaAssetStatus.Uploading:
                asset.MarkProcessing();
                return true;
            default:
                return false;
        }
    }
}
