using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Contracts;

/// <summary>
/// Virus scanner abstraction for episode attachments (P4-03).
/// A real scanning engine (e.g. ClamAV, AWS GuardDuty, or Cloud Storage Malware Scanner)
/// can be plugged in without touching catalog handlers.
/// </summary>
public interface IAttachmentVirusScanner
{
    Task<Result> ScanAsync(string fileName, Stream stream, CancellationToken cancellationToken);

    Task<Result> ScanBytesAsync(string fileName, ReadOnlyMemory<byte> headerOrContent, CancellationToken cancellationToken);
}
