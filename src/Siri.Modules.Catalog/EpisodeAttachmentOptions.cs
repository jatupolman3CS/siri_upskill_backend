using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Catalog;

/// <summary>
/// Configuration options for teaching-material attachments — files an instructor uploads to an episode
/// (P4-03) or to a live session (P4-03c). Despite the name this also covers live-session materials; the
/// section key <see cref="SectionName"/> is kept so existing deployments keep working.
/// </summary>
public sealed class EpisodeAttachmentOptions
{
    public const string SectionName = "Catalog:Attachments";

    /// <summary>
    /// The largest request body the upload endpoints accept (they carry a static
    /// <c>RequestSizeLimit</c> of <see cref="HardMaxFileSizeBytes"/> plus multipart overhead, so this value
    /// can only tighten that, never exceed it).
    /// </summary>
    public const long HardMaxFileSizeBytes = 100L * 1024 * 1024;

    /// <summary>The static request-body cap on the upload endpoints: the largest allowed file plus 1 MB of multipart framing.</summary>
    public const long MaxUploadRequestBodyBytes = HardMaxFileSizeBytes + 1024 * 1024;

    /// <summary>
    /// Maximum allowed file size in bytes. Defaults to 50MB (52,428,800 bytes).
    /// </summary>
    [Range(1, HardMaxFileSizeBytes, ErrorMessage = "MaxFileSizeBytes must be between 1 byte and 100MB.")]
    public long MaxFileSizeBytes { get; set; } = 52_428_800; // 50MB

    /// <summary>
    /// How long a download link stays valid. Short on purpose (security.md: signed URLs are short-lived):
    /// the link is minted right after the entitlement check, so a learner whose enrollment lapses a minute
    /// later still holds a working URL for at most this long.
    /// </summary>
    [Range(60, 900, ErrorMessage = "DownloadUrlTtlSeconds must be between 60 and 900.")]
    public int DownloadUrlTtlSeconds { get; set; } = 300;

    /// <summary>Most files one episode (or one live session) may carry — bounds abuse of the shared bucket.</summary>
    [Range(1, 200, ErrorMessage = "MaxAttachmentsPerParent must be between 1 and 200.")]
    public int MaxAttachmentsPerParent { get; set; } = 30;
}
