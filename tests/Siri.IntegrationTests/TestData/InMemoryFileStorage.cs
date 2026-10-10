using System.Collections.Concurrent;
using Siri.Integrations.Storage;
using Siri.Modules.Catalog.Contracts;
using Siri.SharedKernel;

namespace Siri.IntegrationTests.TestData;

/// <summary>
/// In-memory stand-in for the Cloudflare R2 <see cref="IFileStorage"/> adapter, so integration tests exercise the
/// real upload/validate/authorize/delete pipeline without touching a bucket. Records every object so a test can
/// assert what was stored and what was cleaned up.
/// </summary>
public sealed class InMemoryFileStorage : IFileStorage
{
    public sealed record StoredObject(byte[] Content, string ContentType, string? DownloadFileName);

    public ConcurrentDictionary<string, StoredObject> Objects { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, every upload fails as if R2 were unreachable.</summary>
    public bool FailUploads { get; set; }

    public async Task<Result<StoredFile>> UploadAsync(
        string key,
        Stream content,
        long contentLength,
        string contentType,
        string? downloadFileName,
        CancellationToken cancellationToken)
    {
        if (FailUploads)
        {
            return Result.Failure<StoredFile>(StorageErrors.OperationFailed());
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Objects[key] = new StoredObject(buffer.ToArray(), contentType, downloadFileName);

        return Result.Success(new StoredFile(key, buffer.Length));
    }

    public Task<Result<string>> GetSignedUrlAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success($"https://r2.example.test/{key}?X-Amz-Expires={(int)timeToLive.TotalSeconds}&X-Amz-Signature=fake"));

    public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Objects.TryRemove(key, out _);
        return Task.FromResult(Result.Success());
    }
}

/// <summary>A scanner that passes everything - tests that care about scanning use their own double.</summary>
public sealed class AcceptAllVirusScanner : IAttachmentVirusScanner
{
    public Task<Result> ScanAsync(string fileName, Stream stream, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

    public Task<Result> ScanBytesAsync(string fileName, ReadOnlyMemory<byte> headerOrContent, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());
}
