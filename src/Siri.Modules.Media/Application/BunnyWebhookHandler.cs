using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Video.Bunny;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

public sealed record BunnyWebhookPayload(
    long? VideoLibraryId,
    string? VideoGuid,
    int? Status,
    string? ThumbnailFileName,
    int? Duration);

/// <summary>
/// Handles incoming webhook events from Bunny Stream for video transcoding and status updates.
/// </summary>
public sealed class BunnyWebhookHandler(
    IMediaAssetRepository repository,
    IOptions<VideoProviderOptions> options,
    IClock clock,
    ILogger<BunnyWebhookHandler> logger)
{
    // Bunny Stream status codes:
    // 0 = Created, 1 = Uploaded, 2 = Processing, 3 = Transcoding, 4 = Finished/Ready, 5 = Error, 6 = UploadFailed
    public const int StatusCreated = 0;
    public const int StatusUploaded = 1;
    public const int StatusProcessing = 2;
    public const int StatusTranscoding = 3;
    public const int StatusFinished = 4;
    public const int StatusError = 5;
    public const int StatusUploadFailed = 6;

    public async Task<Result> HandleWebhookAsync(
        BunnyWebhookPayload payload,
        string? authHeader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Basic verification if token configured
        if (string.IsNullOrWhiteSpace(payload.VideoGuid))
        {
            logger.LogWarning("Bunny webhook received with empty VideoGuid.");
            return Result.Failure(DomainError.Validation("VideoGuid cannot be empty."));
        }

        var asset = await repository.GetByProviderAssetIdAsync(payload.VideoGuid, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            logger.LogWarning("Bunny webhook: asset for VideoGuid {VideoGuid} not found in database.", payload.VideoGuid);
            return Result.Failure(DomainError.NotFound($"Asset for {payload.VideoGuid} was not found."));
        }

        logger.LogInformation("Processing Bunny webhook for asset {AssetId} (VideoGuid {VideoGuid}) with status {Status}.",
            asset.MEDIA_ASSET_ID, payload.VideoGuid, payload.Status);

        switch (payload.Status)
        {
            case StatusProcessing:
            case StatusTranscoding:
                if (asset.STATUS == MediaAssetStatus.Uploading)
                {
                    asset.MarkProcessing();
                }
                break;

            case StatusFinished:
                var thumbnailUrl = !string.IsNullOrWhiteSpace(payload.ThumbnailFileName)
                    ? $"https://{options.Value.CdnHostname}/{payload.VideoGuid}/{payload.ThumbnailFileName}"
                    : $"https://{options.Value.CdnHostname}/{payload.VideoGuid}/thumbnail.jpg";

                asset.MarkReady(
                    playbackId: payload.VideoGuid,
                    durationSeconds: payload.Duration ?? 0,
                    thumbnailUrl: thumbnailUrl,
                    clock: clock);
                break;

            case StatusError:
            case StatusUploadFailed:
                asset.MarkFailed($"Transcoding failed with Bunny status {payload.Status}.");
                break;

            default:
                logger.LogInformation("Bunny webhook status {Status} ignored for asset {AssetId}.", payload.Status, asset.MEDIA_ASSET_ID);
                break;
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
