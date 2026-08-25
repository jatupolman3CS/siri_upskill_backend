using Siri.SharedKernel;

namespace Siri.Integrations.Storage;

/// <summary>
/// Abstraction over blob/object storage (course assets, CMS media, exported files). The real
/// adapter ships in a later phase — this is the interface stub only.
/// </summary>
public interface IFileStorage
{
    Task<Result<StoredFile>> UploadAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);

    Task<Result<string>> GetSignedUrlAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(string key, CancellationToken cancellationToken);
}

public sealed record StoredFile(string Key, string Url, long SizeBytes);
