using Siri.Integrations.Video;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

/// <summary>
/// Orchestrates playback token issuance and audit logging via <see cref="IPlaybackSessionRepository"/> and <see cref="IVideoProvider"/>.
/// Supports both enrolled learners and free preview episodes for un-enrolled learners or guests.
/// </summary>
public sealed class PlaybackSessionService(
    IPlaybackSessionRepository sessionRepository,
    IMediaAssetRepository assetRepository,
    IVideoProvider videoProvider,
    ILearningAccessContract learningAccessContract,
    ICatalogPriceContract catalogPriceContract,
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

        var watermarkUserId = effectiveUserId == Guid.Empty ? "Guest" : effectiveUserId.ToString();
        var watermarkPayload = $"SIRI UpSkill · {watermarkUserId} · {clock.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";

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
}
