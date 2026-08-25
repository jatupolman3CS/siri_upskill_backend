using Siri.Modules.Media.Domain;

namespace Siri.Modules.Media.Application;

/// <summary>Wire shape for a <see cref="MEDIA_UPLOAD_SESSION"/> — normal PascalCase, same DTO exemption
/// <see cref="MediaAssetResponse"/>'s own doc comment explains.</summary>
public sealed record MediaUploadSessionResponse(
    Guid Id,
    Guid MediaAssetId,
    string UploadUrl,
    DateTime ExpiresAtUtc,
    MediaUploadSessionStatus Status,
    DateTime CreatedAtUtc);

public static class MediaUploadSessionResponseExtensions
{
    /// <summary>Real (not stubbed) — see <see cref="MediaAssetResponseExtensions.ToResponse"/>'s own doc
    /// comment for why.</summary>
    public static MediaUploadSessionResponse ToResponse(this MEDIA_UPLOAD_SESSION uploadSession) =>
        new(
            uploadSession.MEDIA_UPLOAD_SESSION_ID,
            uploadSession.MEDIA_ASSET_ID,
            uploadSession.UPLOAD_URL,
            uploadSession.EXPIRES_AT_UTC,
            uploadSession.STATUS,
            uploadSession.CreatedAtUtc);
}
