using Microsoft.Extensions.Logging;
using Siri.Integrations.Video;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Media.Application;

/// <summary>
/// <see cref="IMediaIngestContract"/> (P11-13): puts a file the server already holds on the video provider and registers it as a media asset owned by a
/// given user — the server-side twin of the browser upload (<see cref="MediaAssetService"/> + <see cref="MediaUploadSessionService"/>), which is unchanged.
/// <para>
/// Order: create the provider video, <b>save the asset row first</b> (status <see cref="MediaAssetStatus.Uploading"/>), stream the bytes, then
/// <see cref="MEDIA_ASSET.MarkProcessing"/> and save. Saving the row before the (long) upload is deliberate: if the process dies mid-transfer the provider
/// video is not an untraceable orphan — a row exists, and the owner can delete it through the ordinary media-asset delete, which also removes the provider
/// copy. The existing transcode poll job and Bunny webhook then take a <see cref="MediaAssetStatus.Processing"/> asset to <see cref="MediaAssetStatus.Ready"/>
/// exactly as they do for a browser upload — nothing here duplicates that.
/// </para>
/// <para>
/// On <b>any</b> failure (provider refused, network, cancellation, database) the provider video is deleted best effort and the row removed, so a retry starts
/// clean. If the provider refuses the delete the row is kept and marked <see cref="MediaAssetStatus.Failed"/> instead (the same rule
/// <see cref="MediaAssetService.DeleteAsync"/> follows: never drop the only record of a video the provider still hosts). Nothing here logs a provider id,
/// file name or title — only error codes.
/// </para>
/// </summary>
public sealed class MediaIngestContractService(
    IMediaAssetRepository repository,
    IVideoProvider videoProvider,
    ILogger<MediaIngestContractService> logger) : IMediaIngestContract
{
    /// <summary>Same provider literal <see cref="MediaAssetService.CreateAsync"/> stores.</summary>
    internal const string ProviderName = "BunnyStream";

    /// <summary>Cleanup runs even when the caller's token is already cancelled, but never longer than this.</summary>
    internal static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(30);

    public async Task<Result<Guid>> IngestAsync(Guid ownerUserId, string title, Stream content, long? contentLength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (ownerUserId == Guid.Empty)
        {
            return Result.Failure<Guid>(DomainError.Validation("The asset owner is required."));
        }

        var cleanTitle = CleanTitle(title);
        if (cleanTitle is null)
        {
            return Result.Failure<Guid>(DomainError.Validation("The asset title is required."));
        }

        if (contentLength is < 0)
        {
            return Result.Failure<Guid>(DomainError.Validation("The content length cannot be negative."));
        }

        var created = await videoProvider.CreateVideoAsync(cleanTitle, cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        var providerVideoId = created.Value.ProviderVideoId;
        MEDIA_ASSET? asset = null;
        var persisted = false;
        var succeeded = false;
        try
        {
            asset = MEDIA_ASSET.Create(ProviderName, providerVideoId, ownerUserId, drmEnabled: true);
            repository.Add(asset);
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            persisted = true;

            var upload = await videoProvider.UploadVideoAsync(providerVideoId, content, contentLength, cancellationToken).ConfigureAwait(false);
            if (upload.IsFailure)
            {
                logger.LogWarning("Media ingest: the video provider refused the upload ({ErrorCode}).", upload.Error.Code);
                return Result.Failure<Guid>(upload.Error);
            }

            asset.MarkProcessing();
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            succeeded = true;
            return Result.Success(asset.MEDIA_ASSET_ID);
        }
        finally
        {
            if (!succeeded)
            {
                await CleanUpAsync(providerVideoId, asset, persisted).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Best effort and never throws (it runs in a <c>finally</c>, where a second exception would hide the real failure).</summary>
    private async Task CleanUpAsync(string providerVideoId, MEDIA_ASSET? asset, bool persisted)
    {
        using var budget = new CancellationTokenSource(CleanupBudget);
        try
        {
            var deleted = await videoProvider.DeleteVideoAsync(providerVideoId, budget.Token).ConfigureAwait(false);

            if (asset is null)
            {
                if (deleted.IsFailure)
                {
                    logger.LogWarning("Media ingest cleanup: the provider video could not be deleted ({ErrorCode}).", deleted.Error.Code);
                }

                return;
            }

            if (deleted.IsSuccess)
            {
                // An asset that was never saved is only detached by Remove; a saved one is deleted.
                repository.Remove(asset);
                if (persisted)
                {
                    await repository.SaveChangesAsync(budget.Token).ConfigureAwait(false);
                }

                return;
            }

            logger.LogWarning("Media ingest cleanup: the provider video could not be deleted ({ErrorCode}); keeping the asset row as Failed.", deleted.Error.Code);
            if (persisted && asset.STATUS is MediaAssetStatus.Uploading or MediaAssetStatus.Processing)
            {
                asset.MarkFailed("Server-side upload failed and the provider video could not be removed.");
                await repository.SaveChangesAsync(budget.Token).ConfigureAwait(false);
            }
            else if (!persisted)
            {
                repository.Remove(asset);
            }
        }
        catch (Exception ex)
        {
            // Logged by type only: the message of a provider/database exception can carry ids or SQL.
            logger.LogWarning("Media ingest cleanup did not complete: {ExceptionType}.", ex.GetType().Name);
        }
    }

    /// <summary>Trimmed, at most 200 characters (the media-asset title limit), never cut in the middle of a surrogate pair; <c>null</c> when blank.</summary>
    private static string? CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var trimmed = title.Trim();
        if (trimmed.Length <= CreateMediaAssetValidator.MaxTitleLength)
        {
            return trimmed;
        }

        var cut = char.IsHighSurrogate(trimmed[CreateMediaAssetValidator.MaxTitleLength - 1])
            ? CreateMediaAssetValidator.MaxTitleLength - 1
            : CreateMediaAssetValidator.MaxTitleLength;
        return trimmed[..cut];
    }
}
