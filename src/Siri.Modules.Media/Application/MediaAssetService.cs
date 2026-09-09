using Siri.Integrations.Video;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

/// <summary>
/// Orchestrates <see cref="Domain.MEDIA_ASSET"/> use cases through <see cref="IMediaAssetRepository"/> and
/// <see cref="IVideoProvider"/>.
/// </summary>
public sealed class MediaAssetService(
    IMediaAssetRepository repository,
    IVideoProvider videoProvider,
    IClock clock)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Creates a new asset (Uploading status) owned by <paramref name="uploadedByUserId"/>.</summary>
    public async Task<Result<MediaAssetResponse>> CreateAsync(Guid uploadedByUserId, CreateMediaAssetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var videoCreation = await videoProvider.CreateVideoAsync(command.Title, cancellationToken).ConfigureAwait(false);
        if (!videoCreation.IsSuccess)
        {
            return Result.Failure<MediaAssetResponse>(videoCreation.Error);
        }

        var asset = MEDIA_ASSET.Create(
            provider: "BunnyStream",
            providerAssetId: videoCreation.Value.ProviderVideoId,
            uploadedByUserId: uploadedByUserId,
            drmEnabled: true);

        repository.Add(asset);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(asset.ToResponse());
    }

    /// <summary>Ownership check (security.md's IDOR rule): only <paramref name="callerUserId"/>'s own assets resolve.</summary>
    public async Task<Result<MediaAssetResponse>> GetByIdAsync(Guid callerUserId, Guid id, CancellationToken cancellationToken)
    {
        var asset = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return Result.Failure<MediaAssetResponse>(DomainError.NotFound("Media asset was not found."));
        }

        if (asset.UPLOADED_BY_USER_ID != callerUserId)
        {
            return Result.Failure<MediaAssetResponse>(DomainError.Forbidden("You do not own this media asset."));
        }

        if (asset.STATUS is MediaAssetStatus.Uploading or MediaAssetStatus.Processing)
        {
            var statusResult = await videoProvider.GetStatusAsync(asset.PROVIDER_ASSET_ID, cancellationToken).ConfigureAwait(false);
            if (statusResult.IsFailure)
            {
                return Result.Failure<MediaAssetResponse>(statusResult.Error);
            }

            if (MediaAssetStatusUpdater.Apply(asset, statusResult.Value, clock))
            {
                await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success(asset.ToResponse());
    }

    public async Task<PagedResult<MediaAssetResponse>> GetMyAssetsAsync(Guid callerUserId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var (items, totalCount) = await repository.GetPagedByUploaderAsync(
            callerUserId, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);

        var mapped = items.Select(a => a.ToResponse()).ToList();
        return PagedResult<MediaAssetResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    /// <summary>System/provider-driven status transition.</summary>
    public async Task<Result<MediaAssetResponse>> UpdateStatusAsync(Guid id, UpdateMediaAssetStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var asset = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return Result.Failure<MediaAssetResponse>(DomainError.NotFound("Media asset was not found."));
        }

        try
        {
            switch (command.Status)
            {
                case MediaAssetStatus.Processing:
                    asset.MarkProcessing();
                    break;
                case MediaAssetStatus.Ready:
                    asset.MarkReady(command.PlaybackId, command.DurationSeconds ?? 0, command.ThumbnailUrl, clock);
                    break;
                case MediaAssetStatus.Failed:
                    asset.MarkFailed(command.ErrorMessage ?? "Processing failed.");
                    break;
                default:
                    return Result.Failure<MediaAssetResponse>(DomainError.Validation($"Invalid status transition to {command.Status}."));
            }
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<MediaAssetResponse>(DomainError.Conflict(ex.Message));
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(asset.ToResponse());
    }

    /// <summary>Ownership check (security.md's IDOR rule) and provider deletion.</summary>
    public async Task<Result> DeleteAsync(Guid callerUserId, Guid id, CancellationToken cancellationToken)
    {
        var asset = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return Result.Failure(DomainError.NotFound("Media asset was not found."));
        }

        if (asset.UPLOADED_BY_USER_ID != callerUserId)
        {
            return Result.Failure(DomainError.Forbidden("You do not own this media asset."));
        }

        await videoProvider.DeleteVideoAsync(asset.PROVIDER_ASSET_ID, cancellationToken).ConfigureAwait(false);

        repository.Remove(asset);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
