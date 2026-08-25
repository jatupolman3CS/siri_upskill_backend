namespace Siri.Modules.Media.Domain;

/// <summary>
/// Lifecycle of a <see cref="MEDIA_UPLOAD_SESSION"/> — see <see cref="MediaAssetStatus"/>'s own doc comment
/// for why this enum type/members stay PascalCase despite living on an UPPERCASE entity property.
/// </summary>
public enum MediaUploadSessionStatus
{
    Pending,
    Completed,
    Expired,
}
