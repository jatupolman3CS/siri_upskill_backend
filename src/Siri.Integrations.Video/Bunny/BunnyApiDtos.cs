using System.Text.Json.Serialization;

namespace Siri.Integrations.Video.Bunny;

/// <summary>
/// Internal DTOs for deserializing Bunny Stream API responses. These are not part of the public
/// contract — external callers see <see cref="VideoAsset"/>/<see cref="VideoStatus"/>/<see cref="SignedPlaybackUrl"/>
/// from <see cref="IVideoProvider"/> only.
/// </summary>
internal sealed class BunnyVideoResponse
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("length")]
    public double Length { get; set; }

    [JsonPropertyName("thumbnailFileName")]
    public string ThumbnailFileName { get; set; } = string.Empty;
}

/// <summary>
/// Bunny Stream <c>VideoModelStatus</c> codes (from the "Get Video" API reference). Codes 7/8 (JIT
/// segmenting/playlists) are not listed here — they fall into the "still processing" default.
/// </summary>
internal static class BunnyVideoStatus
{
    /// <summary>Video object exists but no bytes have been received yet.</summary>
    public const int Created = 0;

    /// <summary>The upload has FINISHED (Bunny calls it "Uploaded") and the video waits for the encoder.
    /// It is not "uploading": treating it as such made the upload-complete step answer 409 for a video
    /// whose bytes had all arrived.</summary>
    public const int Uploaded = 1;

    /// <summary>Video is being processed/transcoded.</summary>
    public const int Processing = 2;

    /// <summary>Video is being transcoded.</summary>
    public const int Transcoding = 3;

    /// <summary>Video is ready for playback.</summary>
    public const int Finished = 4;

    /// <summary>Video encoding has failed.</summary>
    public const int Error = 5;

    /// <summary>Upload has failed.</summary>
    public const int UploadFailed = 6;
}
