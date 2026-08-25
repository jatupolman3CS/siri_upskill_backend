using Siri.Integrations.Video;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

/// <summary>
/// Orchestrates playback token issuance and audit logging via <see cref="IPlaybackSessionRepository"/> and <see cref="IVideoProvider"/>.
/// </summary>
public sealed class PlaybackSessionService(
    IPlaybackSessionRepository sessionRepository,
    IMediaAssetRepository assetRepository,
    IVideoProvider videoProvider,
    ILearningAccessContract learningAccessContract,
    IClock clock)
{
    private static readonly TimeSpan PlaybackUrlLifetime = TimeSpan.FromMinutes(5);

    public async Task<Result<PlaybackSessionResponse>> CreateAsync(
        Guid userId,
        Guid sessionId,
        string? ipAddress,
        CreatePlaybackSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (userId == Guid.Empty)
        {
            return Result.Failure<PlaybackSessionResponse>(DomainError.Forbidden("User must be authenticated."));
        }

        // SE-01 / SE-02 / P2-04: Check active enrollment or free preview access
        var hasAccess = await learningAccessContract.CanUserAccessEpisodeAsync(
            userId, command.EpisodeId, cancellationToken).ConfigureAwait(false);

        if (!hasAccess)
        {
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

        var watermarkPayload = $"SIRI UpSkill · {userId} · {clock.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";

        var session = PLAYBACK_SESSION.Issue(
            userId: userId,
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
