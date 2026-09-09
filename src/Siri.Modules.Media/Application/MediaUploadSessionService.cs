using Siri.Integrations.Video;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

/// <summary>
/// Orchestrates <see cref="Domain.MEDIA_UPLOAD_SESSION"/> use cases through
/// <see cref="IMediaUploadSessionRepository"/> and <see cref="IVideoProvider"/>.
/// </summary>
public sealed class MediaUploadSessionService(
    IMediaUploadSessionRepository sessionRepository,
    IMediaAssetRepository assetRepository,
    IVideoProvider videoProvider,
    IClock clock)
{
    /// <summary>Starts a new upload session for <paramref name="mediaAssetId"/> with IDOR check.</summary>
    public async Task<Result<MediaUploadSessionResponse>> CreateAsync(Guid callerUserId, Guid mediaAssetId, CancellationToken cancellationToken)
    {
        var asset = await assetRepository.GetByIdAsync(mediaAssetId, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return Result.Failure<MediaUploadSessionResponse>(DomainError.NotFound("Media asset was not found."));
        }

        if (asset.UPLOADED_BY_USER_ID != callerUserId)
        {
            return Result.Failure<MediaUploadSessionResponse>(DomainError.Forbidden("You do not own this media asset."));
        }

        if (asset.STATUS != MediaAssetStatus.Uploading)
        {
            return Result.Failure<MediaUploadSessionResponse>(DomainError.Conflict("Only an uploading media asset can receive a new upload session."));
        }

        var uploadUrlResult = await videoProvider.GetUploadUrlAsync(asset.PROVIDER_ASSET_ID, cancellationToken).ConfigureAwait(false);
        if (!uploadUrlResult.IsSuccess)
        {
            return Result.Failure<MediaUploadSessionResponse>(uploadUrlResult.Error);
        }

        var session = MEDIA_UPLOAD_SESSION.Create(
            mediaAssetId: mediaAssetId,
            uploadUrl: uploadUrlResult.Value.UploadUrl,
            expiresAtUtc: uploadUrlResult.Value.ExpiresAtUtc);

        sessionRepository.Add(session);
        await sessionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(session.ToResponse());
    }

    public async Task<Result<MediaUploadSessionResponse>> GetByIdAsync(Guid callerUserId, Guid id, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure<MediaUploadSessionResponse>(DomainError.NotFound("Upload session was not found."));
        }

        var asset = await assetRepository.GetByIdAsync(session.MEDIA_ASSET_ID, cancellationToken).ConfigureAwait(false);
        if (asset is null || asset.UPLOADED_BY_USER_ID != callerUserId)
        {
            return Result.Failure<MediaUploadSessionResponse>(DomainError.Forbidden("You do not own this upload session."));
        }

        return Result.Success(session.ToResponse());
    }

    /// <summary>Client-signalled "I finished uploading" callback.</summary>
    public async Task<Result<MediaUploadSessionResponse>> CompleteAsync(Guid callerUserId, Guid id, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure<MediaUploadSessionResponse>(DomainError.NotFound("Upload session was not found."));
        }

        var asset = await assetRepository.GetByIdAsync(session.MEDIA_ASSET_ID, cancellationToken).ConfigureAwait(false);
        if (asset is null || asset.UPLOADED_BY_USER_ID != callerUserId)
        {
            return Result.Failure<MediaUploadSessionResponse>(DomainError.Forbidden("You do not own this upload session."));
        }

        if (session.STATUS == MediaUploadSessionStatus.Completed)
        {
            return Result.Success(session.ToResponse());
        }

        if (session.STATUS == MediaUploadSessionStatus.Expired || session.EXPIRES_AT_UTC <= clock.UtcNow)
        {
            if (session.STATUS == MediaUploadSessionStatus.Pending)
            {
                session.MarkExpired();
                await sessionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            return Result.Failure<MediaUploadSessionResponse>(DomainError.Conflict("The upload session has expired."));
        }

        if (asset.STATUS == MediaAssetStatus.Failed)
        {
            return Result.Failure<MediaUploadSessionResponse>(DomainError.Conflict("The video provider could not process this upload."));
        }

        if (asset.STATUS != MediaAssetStatus.Ready)
        {
            var statusResult = await videoProvider.GetStatusAsync(asset.PROVIDER_ASSET_ID, cancellationToken).ConfigureAwait(false);
            if (statusResult.IsFailure)
            {
                return Result.Failure<MediaUploadSessionResponse>(statusResult.Error);
            }

            if (statusResult.Value.Status == VideoProcessingStatus.Uploading)
            {
                return Result.Failure<MediaUploadSessionResponse>(DomainError.Conflict("The video provider has not finished receiving this upload yet."));
            }

            if (MediaAssetStatusUpdater.Apply(asset, statusResult.Value, clock))
            {
                await assetRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            if (asset.STATUS == MediaAssetStatus.Failed)
            {
                return Result.Failure<MediaUploadSessionResponse>(DomainError.Conflict("The video provider could not process this upload."));
            }
        }

        session.Complete();
        await sessionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(session.ToResponse());
    }
}
