using Siri.SharedKernel;

namespace Siri.Integrations.Video;

/// <summary>
/// Abstraction over the video hosting/DRM provider (ARCHITECTURE.md §4). The real adapter
/// (Bunny Stream) ships in a later phase — this is the interface stub only, so modules can be
/// written against the contract now without depending on a concrete provider.
/// </summary>
public interface IVideoProvider
{
    Task<Result<VideoAsset>> CreateVideoAsync(string title, CancellationToken cancellationToken);

    Task<Result<VideoUploadUrl>> GetUploadUrlAsync(string providerVideoId, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads the bytes of an already-created video straight from the server (P11-13: a recording the platform pulled from Google).
    /// The body is streamed, never buffered, and is <b>not retried</b> here (a consumed stream cannot be replayed) — the caller retries the whole
    /// transfer. The provider consumes <paramref name="content"/> and disposes it together with the request. Bunny:
    /// <c>PUT /library/{libraryId}/videos/{videoId}</c> with the <c>AccessKey</c> header.
    /// </summary>
    Task<Result> UploadVideoAsync(string providerVideoId, Stream content, long? contentLength, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<VideoStatus>> GetStatusAsync(string providerVideoId, CancellationToken cancellationToken);

    Task<Result> DeleteVideoAsync(string providerVideoId, CancellationToken cancellationToken);

    Task<Result<SignedPlaybackUrl>> GetSignedPlaybackUrlAsync(
        string providerVideoId,
        TimeSpan timeToLive,
        CancellationToken cancellationToken);
}

public sealed record VideoAsset(string ProviderVideoId, string Title);

public sealed record VideoUploadUrl(string UploadUrl, DateTime ExpiresAtUtc);

public enum VideoProcessingStatus
{
    Uploading,
    Processing,
    Ready,
    Failed,
}

public sealed record VideoStatus(string ProviderVideoId, VideoProcessingStatus Status, TimeSpan? Duration);

public sealed record SignedPlaybackUrl(string ManifestUrl, DateTime ExpiresAtUtc);
