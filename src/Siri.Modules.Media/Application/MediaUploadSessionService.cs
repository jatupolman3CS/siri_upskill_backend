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
    IVideoProvider videoProvider)
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

        try
        {
            session.Complete();
            if (asset.STATUS == MediaAssetStatus.Uploading)
            {
                asset.MarkProcessing();
                await assetRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<MediaUploadSessionResponse>(DomainError.Conflict(ex.Message));
        }

        await sessionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(session.ToResponse());
    }
}
