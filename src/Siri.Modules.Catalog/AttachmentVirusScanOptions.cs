using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Catalog;

/// <summary>
/// What an episode-attachment upload does about virus scanning while no scanning engine is integrated.
/// </summary>
public enum AttachmentVirusScanMode
{
    /// <summary>
    /// Every upload must be scanned. With no scanning engine registered the upload is refused
    /// (<c>attachment.virus_scanner_not_configured</c>, HTTP 503) — a file is never reported "clean"
    /// without actually having been scanned. This is the default.
    /// </summary>
    Required = 0,

    /// <summary>
    /// The operator explicitly accepts unscanned uploads (owner decision 2026-10-10, docs/DECISIONS.md Q9: skip the scan
    /// step for now - allowed in Production too, see <c>ProductionConfigurationGuard</c>). Files are accepted and every one is logged as
    /// NOT scanned; nothing is ever reported as scanned or clean.
    /// </summary>
    Disabled = 1,
}

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> (<c>Attachments:VirusScan</c>).
/// </summary>
public sealed class AttachmentVirusScanOptions
{
    public const string SectionName = "Attachments:VirusScan";

    [EnumDataType(typeof(AttachmentVirusScanMode))]
    public AttachmentVirusScanMode Mode { get; set; } = AttachmentVirusScanMode.Required;
}
