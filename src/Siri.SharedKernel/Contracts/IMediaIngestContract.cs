namespace Siri.SharedKernel.Contracts;

/// <summary>
/// Server-side video ingest, for callers that already hold the bytes (P11-13: the platform pulls a Google Meet recording and
/// puts it on the video provider). Implemented by the Media module. The browser upload path (<c>MediaAssetService</c> +
/// <c>MediaUploadSessionService</c> + TUS) is unchanged.
/// </summary>
public interface IMediaIngestContract
{
    /// <summary>
    /// Creates a media asset owned by <paramref name="ownerUserId"/> (so the ordinary "the asset must be the caller's own" rules keep
    /// holding), uploads <paramref name="content"/> to the video provider by streaming it (never buffering the whole file), and leaves
    /// the asset in <c>Processing</c> so the existing transcode poll / webhook moves it to <c>Ready</c>.
    /// <para>
    /// Success carries the new asset id. On any failure nothing is left behind: the provider video is deleted (best effort) and the
    /// asset row removed, so a retry starts clean. The caller owns <paramref name="content"/> and disposes it. Failures are
    /// <see cref="Result"/>s (<c>video.provider_not_configured</c> when Bunny is not configured, <c>video.provider_error</c>, ...).
    /// </para>
    /// </summary>
    Task<Result<Guid>> IngestAsync(Guid ownerUserId, string title, Stream content, long? contentLength, CancellationToken cancellationToken);
}
