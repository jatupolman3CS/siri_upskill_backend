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
