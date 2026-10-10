using Siri.SharedKernel;

namespace Siri.Integrations.Storage;

/// <summary>
/// Abstraction over private blob/object storage (teaching materials today; CMS media and exported files
/// later). The one real adapter is <see cref="R2FileStorage"/> (Cloudflare R2, S3-compatible API).
/// <para>
/// Objects are always private: nothing is reachable by URL unless the caller first decided the requester
/// may have it and then asked for a short-lived <see cref="GetSignedUrlAsync"/>. Keys are chosen by the
/// server, never by a client (see <c>docs/contracts/P4-03c-teaching-materials-r2.md</c>).
/// </para>
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Stores <paramref name="content"/> under <paramref name="key"/>, replacing any object already there.
    /// <paramref name="contentLength"/> is required because the stream may not be seekable. When
    /// <paramref name="downloadFileName"/> is given the object is stored with a
    /// <c>Content-Disposition: attachment</c> header carrying that name, so a browser always downloads it
    /// instead of rendering it inline (an uploaded HTML/SVG-ish file can never run in our origin).
    /// </summary>
    Task<Result<StoredFile>> UploadAsync(
        string key,
        Stream content,
        long contentLength,
        string contentType,
        string? downloadFileName,
        CancellationToken cancellationToken);

    /// <summary>A time-limited GET URL for <paramref name="key"/>. Does not check that the object exists.</summary>
    Task<Result<string>> GetSignedUrlAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken);

    /// <summary>Deletes <paramref name="key"/>. Idempotent: deleting a key that does not exist succeeds.</summary>
    Task<Result> DeleteAsync(string key, CancellationToken cancellationToken);
}

public sealed record StoredFile(string Key, long SizeBytes);
