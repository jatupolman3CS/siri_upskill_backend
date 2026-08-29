using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Catalog;

/// <summary>
/// Configuration options for episode file attachments (P4-03).
/// </summary>
public sealed class EpisodeAttachmentOptions
{
    public const string SectionName = "Catalog:Attachments";

    /// <summary>
    /// Maximum allowed file size in bytes. Defaults to 50MB (52,428,800 bytes).
    /// </summary>
    [Range(1, 500 * 1024 * 1024, ErrorMessage = "MaxFileSizeBytes must be between 1 byte and 500MB.")]
    public long MaxFileSizeBytes { get; set; } = 52_428_800; // 50MB
}
