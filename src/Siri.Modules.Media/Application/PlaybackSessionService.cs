using Microsoft.Extensions.Logging;
using Siri.Integrations.Video;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

/// <summary>
/// Orchestrates playback token issuance and audit logging via <see cref="IPlaybackSessionRepository"/> and <see cref="IVideoProvider"/>.
/// Supports both enrolled learners and free preview episodes for un-enrolled learners or guests.
/// The watermark payload (SE-02 / Q8) names the viewer — display name + email + timestamp — and is built here on the
/// server only, from the authenticated user id, via <see cref="WatermarkPayloadBuilder"/>.
/// </summary>
public sealed class PlaybackSessionService(
    IPlaybackSessionRepository sessionRepository,
    IMediaAssetRepository assetRepository,
    IVideoProvider videoProvider,
    ILearningAccessContract learningAccessContract,
    ICatalogPriceContract catalogPriceContract,
    IUserContactReader userContactReader,
    ILogger<PlaybackSessionService> logger,
    IClock clock)
{
    private static readonly TimeSpan PlaybackUrlLifetime = TimeSpan.FromMinutes(5);
    private static readonly Guid GuestUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public async Task<Result<PlaybackSessionResponse>> GetByEpisodeIdAsync(
        Guid? userId,
        Guid sessionId,
        string? ipAddress,
        string? deviceId,
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        if (episodeId == Guid.Empty)
        {
            return Result.Failure<PlaybackSessionResponse>(DomainError.Validation("Episode ID cannot be empty."));
        }

        var mediaAssetId = await catalogPriceContract.GetMediaAssetIdForEpisodeAsync(episodeId, cancellationToken).ConfigureAwait(false);
        if (mediaAssetId is null || mediaAssetId.Value == Guid.Empty)
        {
            return Result.Failure<PlaybackSessionResponse>(DomainError.NotFound("ไม่พบวิดีโอของบทเรียนนี้"));
        }

        var command = new CreatePlaybackSessionCommand(episodeId, mediaAssetId.Value, deviceId);
        return await CreateAsync(userId, sessionId, ipAddress, command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PlaybackSessionResponse>> CreateAsync(
        Guid? userId,
        Guid sessionId,
        string? ipAddress,
        CreatePlaybackSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var effectiveUserId = userId.GetValueOrDefault(Guid.Empty);

        // SE-01 / SE-02 / P2-04 / P2-25: Check active enrollment or free preview access
        var hasAccess = await learningAccessContract.CanUserAccessEpisodeAsync(
            effectiveUserId, command.EpisodeId, cancellationToken).ConfigureAwait(false);

        if (!hasAccess)
        {
            if (effectiveUserId == Guid.Empty)
            {
                return Result.Failure<PlaybackSessionResponse>(
                    DomainError.Forbidden("กรุณาเข้าสู่ระบบ หรือลงทะเบียนเรียนเพื่อรับชมบทเรียนนี้"));
            }

            return Result.Failure<PlaybackSessionResponse>(
                DomainError.Forbidden("คุณยังไม่ได้ลงทะเบียนเรียนในบทเรียนนี้ หรือสิทธิ์การเข้าถึงหมดอายุแล้ว"));
        }

        var asset = await assetRepository.GetByIdAsync(command.MediaAssetId, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return Result.Failure<PlaybackSessionResponse>(DomainError.NotFound("Media asset was not found."));
        }

        if (asset.STATUS != MediaAssetStatus.Ready)
        {
            return Result.Failure<PlaybackSessionResponse>(DomainError.Conflict($"Media asset is not ready for playback (Status: {asset.STATUS})."));
        }

        var signedUrlResult = await videoProvider.GetSignedPlaybackUrlAsync(
            asset.PROVIDER_ASSET_ID,
            PlaybackUrlLifetime,
            cancellationToken).ConfigureAwait(false);

        if (!signedUrlResult.IsSuccess)
        {
            return Result.Failure<PlaybackSessionResponse>(signedUrlResult.Error);
        }

        var watermarkPayload = await BuildWatermarkPayloadAsync(effectiveUserId, cancellationToken).ConfigureAwait(false);

        var session = PLAYBACK_SESSION.Issue(
            userId: effectiveUserId == Guid.Empty ? GuestUserId : effectiveUserId,
            episodeId: command.EpisodeId,
            sessionId: sessionId == Guid.Empty ? UuidV7.NewId() : sessionId,
            expiresAtUtc: signedUrlResult.Value.ExpiresAtUtc,
            ipAddress: ipAddress,
            deviceId: command.DeviceId,
            clock: clock);

        sessionRepository.Add(session);
        await sessionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new PlaybackSessionResponse(
            signedUrlResult.Value.ManifestUrl,
            signedUrlResult.Value.ExpiresAtUtc,
            watermarkPayload));
    }

    /// <summary>
    /// SE-02 / Q8: the watermark must identify the viewer. The name and email are read through the read-only
    /// <see cref="IUserContactReader"/> contract using the authenticated user id (never anything the client sent).
    /// A failed or incomplete lookup must not block playback — it degrades to the user-id form instead (see
    /// <see cref="WatermarkPayloadBuilder"/>) and is logged without any name/email.
    /// </summary>
    private async Task<string> BuildWatermarkPayloadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var utcNow = clock.UtcNow;

        if (userId == Guid.Empty)
        {
            return WatermarkPayloadBuilder.ForGuest(utcNow);
        }

        string? displayName = null;
        string? email = null;

        try
        {
            (email, displayName) = await userContactReader
                .GetUserContactInfoAsync(userId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Exception type only: never log the exception message/object next to identity data.
            logger.LogWarning(
                "Watermark contact lookup failed for user {UserId} ({ExceptionType}); falling back to the user-id watermark.",
                userId,
                ex.GetType().Name);
        }

        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning(
                "Watermark identity incomplete for user {UserId} (hasDisplayName={HasDisplayName}, hasEmail={HasEmail}).",
                userId,
                !string.IsNullOrWhiteSpace(displayName),
                !string.IsNullOrWhiteSpace(email));
        }

        return WatermarkPayloadBuilder.ForUser(userId, displayName, email, utcNow);
    }
}
