using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Storage;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.SharedKernel;

namespace Siri.UnitTests.Catalog;

/// <summary>In-memory <see cref="IFileStorage"/> that records every call, for unit tests of the teaching-material pipeline.</summary>
internal sealed class InMemoryFileStorage : IFileStorage
{
    public sealed record StoredObject(byte[] Content, string ContentType, string? DownloadFileName);

    public ConcurrentDictionary<string, StoredObject> Objects { get; } = new(StringComparer.Ordinal);

    public List<string> DeletedKeys { get; } = [];

    public int UploadCalls { get; private set; }

    public bool FailUploads { get; set; }

    public async Task<Result<StoredFile>> UploadAsync(
        string key,
        Stream content,
        long contentLength,
        string contentType,
        string? downloadFileName,
        CancellationToken cancellationToken)
    {
        UploadCalls++;
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
        Task.FromResult(Result.Success($"https://r2.example.test/{key}?ttl={(int)timeToLive.TotalSeconds}"));

    public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        DeletedKeys.Add(key);
        Objects.TryRemove(key, out _);
        return Task.FromResult(Result.Success());
    }
}

internal sealed class StubVirusScanner(Result result) : IAttachmentVirusScanner
{
    public int ScanCalls { get; private set; }

    public static StubVirusScanner Clean() => new(Result.Success());

    public Task<Result> ScanAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        ScanCalls++;
        return Task.FromResult(result);
    }

    public Task<Result> ScanBytesAsync(string fileName, ReadOnlyMemory<byte> headerOrContent, CancellationToken cancellationToken)
    {
        ScanCalls++;
        return Task.FromResult(result);
    }
}

internal sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow => utcNow;
}

internal static class TeachingMaterialTestFactory
{
    public static TeachingMaterialStorage Create(
        IFileStorage? storage = null,
        IAttachmentVirusScanner? scanner = null,
        EpisodeAttachmentOptions? options = null,
        DateTime? now = null) =>
        new(
            storage ?? new InMemoryFileStorage(),
            scanner ?? StubVirusScanner.Clean(),
            Options.Create(options ?? new EpisodeAttachmentOptions()),
            new FixedClock(now ?? new DateTime(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc)),
            NullLogger<TeachingMaterialStorage>.Instance);
}
