using Siri.Modules.Catalog.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// Default no-op implementation of <see cref="IAttachmentVirusScanner"/> used until a specific
/// virus scanning engine is decided and configured (see docs/DECISIONS.md).
/// </summary>
public sealed class NullAttachmentVirusScanner : IAttachmentVirusScanner
{
    public Task<Result> ScanAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }

    public Task<Result> ScanBytesAsync(string fileName, ReadOnlyMemory<byte> headerOrContent, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
