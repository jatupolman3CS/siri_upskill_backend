namespace Siri.Modules.Media.Application;

public sealed record CreatePlaybackSessionCommand(
    Guid EpisodeId,
    Guid MediaAssetId,
    string? DeviceId);

public sealed record PlaybackSessionResponse(
    string ManifestUrl,
    DateTime ExpiresAtUtc,
    string WatermarkPayload);
