using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// <see cref="IAttachmentVirusScanner"/> used while no scanning engine is integrated. It never scans and
/// therefore never reports a file as clean:
/// <list type="bullet">
/// <item><see cref="AttachmentVirusScanMode.Required"/> (default) — every scan fails with
/// <see cref="ScannerNotConfiguredCode"/> (HTTP 503 via the shared <c>_not_configured</c> suffix), so the
/// upload is refused.</item>
/// <item><see cref="AttachmentVirusScanMode.Disabled"/> — the operator explicitly accepted unscanned
/// uploads; the call succeeds only in the sense of "not blocked", and each file is logged as NOT scanned.
/// Production refuses to boot in this mode.</item>
/// </list>
/// Plug a real engine in by registering another <see cref="IAttachmentVirusScanner"/> instead of this one;
/// no handler changes.
/// </summary>
public sealed class UnconfiguredAttachmentVirusScanner(
    IOptions<AttachmentVirusScanOptions> options,
    ILogger<UnconfiguredAttachmentVirusScanner> logger) : IAttachmentVirusScanner
{
    /// <summary>Ends in <see cref="DomainErrorHttpResults.NotConfiguredCodeSuffix"/>, so it maps to HTTP 503.</summary>
    public const string ScannerNotConfiguredCode = "attachment.virus_scanner_not_configured";

    public Task<Result> ScanAsync(string fileName, Stream stream, CancellationToken cancellationToken) =>
        Task.FromResult(Decide());

    public Task<Result> ScanBytesAsync(string fileName, ReadOnlyMemory<byte> headerOrContent, CancellationToken cancellationToken) =>
        Task.FromResult(Decide());

    private Result Decide()
    {
        if (options.Value.Mode == AttachmentVirusScanMode.Disabled)
        {
            logger.LogWarning(
                "Attachment accepted WITHOUT a virus scan: {Option} is {Mode} and no scanning engine is integrated.",
                $"{AttachmentVirusScanOptions.SectionName}:Mode",
                nameof(AttachmentVirusScanMode.Disabled));
            return Result.Success();
        }

        logger.LogError(
            "Attachment upload refused: {Option} is {Mode} but no virus-scanning engine is integrated.",
            $"{AttachmentVirusScanOptions.SectionName}:Mode",
            nameof(AttachmentVirusScanMode.Required));

        return Result.Failure(new DomainError(
            ScannerNotConfiguredCode,
            "Attachment virus scanning is required but no scanning engine is configured."));
    }
}
