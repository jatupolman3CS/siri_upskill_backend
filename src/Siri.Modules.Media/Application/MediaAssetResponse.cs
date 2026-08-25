using Siri.Modules.Media.Domain;

namespace Siri.Modules.Media.Application;

/// <summary>Wire shape for a <see cref="MEDIA_ASSET"/>. Normal PascalCase — DTOs are explicitly exempt from
/// the D-17 UPPERCASE exception (only entity classes/properties that map 1:1 to a table/column go
/// uppercase; see <see cref="MEDIA_ASSET"/>'s own doc comment).</summary>
public sealed record MediaAssetResponse(
    Guid Id,
    string Provider,
    string ProviderAssetId,
    string? PlaybackId,
    MediaAssetStatus Status,
    int? DurationSeconds,
    bool DrmEnabled,
    string? ThumbnailUrl,
    Guid UploadedByUserId,
    DateTime? ReadyAtUtc,
    string? ErrorMessage,
    DateTime CreatedAtUtc);

public static class MediaAssetResponseExtensions
{
    /// <summary>Real (not stubbed) — pure mechanical field mapping, no judgment call for a later task to
    /// make differently. Ready for <see cref="MediaAssetService"/>'s stub methods to call once de-stubbed.</summary>
    public static MediaAssetResponse ToResponse(this MEDIA_ASSET mediaAsset) =>
        new(
            mediaAsset.MEDIA_ASSET_ID,
            mediaAsset.PROVIDER,
            mediaAsset.PROVIDER_ASSET_ID,
            mediaAsset.PLAYBACK_ID,
            mediaAsset.STATUS,
            mediaAsset.DURATION_SECONDS,
            mediaAsset.DRM_ENABLED,
            mediaAsset.THUMBNAIL_URL,
            mediaAsset.UPLOADED_BY_USER_ID,
            mediaAsset.READY_AT_UTC,
            mediaAsset.ERROR_MESSAGE,
            mediaAsset.CreatedAtUtc);
}
