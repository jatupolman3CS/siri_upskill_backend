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
/// Bunny Stream video processing status codes (from API documentation).
/// </summary>
internal static class BunnyVideoStatus
{
    /// <summary>Video has been created but not yet uploaded.</summary>
    public const int Created = 0;

    /// <summary>Video is currently being uploaded.</summary>
    public const int Uploading = 1;

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
