namespace Siri.Modules.Media.Domain;

/// <summary>
/// Lifecycle of a <see cref="MEDIA_ASSET"/> as reported by the video provider (docs/DATABASE.md's "media"
/// section). Enum type and members stay PascalCase per the D-17 UPPERCASE-naming exception's own carve-out
/// (docs/DECISIONS.md D-17, <c>.claude/rules/database.md</c>) — only the property that *holds* the enum on
/// <see cref="MEDIA_ASSET"/> goes uppercase (<see cref="MEDIA_ASSET.STATUS"/>); members still serialize
/// through the host's global <c>JsonStringEnumConverter</c> unchanged, so the wire contract is untouched.
/// </summary>
public enum MediaAssetStatus
{
    Uploading,
    Processing,
    Ready,
    Failed,
}
