using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.SharedKernel;

namespace Siri.Integrations.Video.Bunny;

/// <summary>
/// <see cref="IVideoProvider"/> implementation backed by the Bunny Stream REST API.
/// <para>
/// Uses <see cref="IHttpClientFactory"/> (named client "BunnyStream") for connection pooling and
/// lifetime management per .NET best practices. The API key is attached to every request via the
/// <c>AccessKey</c> header (Bunny's authentication mechanism).
/// </para>
/// <para>
/// Retry/backoff for transient failures is handled manually (3 attempts, exponential backoff) rather
/// than pulling in Polly — keeping the dependency footprint small for this integration project.
/// </para>
/// </summary>
public sealed class BunnyVideoProvider : IVideoProvider
{
    /// <summary>Named <see cref="HttpClient"/> identifier registered via
    /// <see cref="IHttpClientFactory"/>.</summary>
    public const string HttpClientName = "BunnyStream";

    private const string BunnyApiBaseUrl = "https://video.bunnycdn.com";
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VideoProviderOptions _options;
    private readonly ILogger<BunnyVideoProvider> _logger;

    public BunnyVideoProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<VideoProviderOptions> options,
        ILogger<BunnyVideoProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<VideoAsset>> CreateVideoAsync(string title, CancellationToken cancellationToken)
    {
        var url = $"{BunnyApiBaseUrl}/library/{_options.LibraryId}/videos";
        var payload = new { title };

        var response = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = JsonContent.Create(payload);
                return request;
            },
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<VideoAsset>(await MapErrorAsync("CreateVideo", response, cancellationToken).ConfigureAwait(false));
        }

        var bunnyVideo = await response.Content.ReadFromJsonAsync<BunnyVideoResponse>(cancellationToken).ConfigureAwait(false);
        if (bunnyVideo is null || string.IsNullOrEmpty(bunnyVideo.Guid))
        {
            return Result.Failure<VideoAsset>(new DomainError("video.provider_error", "Bunny Stream returned an invalid response when creating a video."));
        }

        return Result.Success(new VideoAsset(bunnyVideo.Guid, bunnyVideo.Title));
    }

    /// <inheritdoc/>
    public Task<Result<VideoUploadUrl>> GetUploadUrlAsync(string providerVideoId, CancellationToken cancellationToken)
    {
        // Bunny Stream uses TUS protocol for uploads. The upload URL is constructed from:
        // - The TUS endpoint: https://video.bunnycdn.com/tusupload
        // - An authorization signature: SHA256(LibraryId + ApiKey + ExpirationTime + VideoId)
        // - Expiration time as a Unix timestamp
        // The frontend TUS client then uses these headers to upload directly to Bunny.

        var expiresAt = DateTime.UtcNow.AddHours(4);
        var expirationTime = new DateTimeOffset(expiresAt).ToUnixTimeSeconds().ToString();

        var signaturePayload = $"{_options.LibraryId}{_options.ApiKey}{expirationTime}{providerVideoId}";
        var signatureBytes = SHA256.HashData(Encoding.UTF8.GetBytes(signaturePayload));
        var signature = Convert.ToHexStringLower(signatureBytes);

        var uploadUrl = $"{BunnyApiBaseUrl}/tusupload";

        // The caller (frontend TUS client) needs these values as headers:
        // AuthorizationSignature, AuthorizationExpire, LibraryId, VideoId
        // We encode them as query parameters in the upload URL so the frontend can parse them out.
        var fullUrl = $"{uploadUrl}?AuthorizationSignature={signature}&AuthorizationExpire={expirationTime}&LibraryId={_options.LibraryId}&VideoId={providerVideoId}";

        return Task.FromResult(Result.Success(new VideoUploadUrl(fullUrl, expiresAt)));
    }

    /// <inheritdoc/>
    public async Task<Result<VideoStatus>> GetStatusAsync(string providerVideoId, CancellationToken cancellationToken)
    {
        var url = $"{BunnyApiBaseUrl}/library/{_options.LibraryId}/videos/{providerVideoId}";

        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Failure<VideoStatus>(DomainError.NotFound($"Video '{providerVideoId}' not found in Bunny Stream."));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<VideoStatus>(await MapErrorAsync("GetStatus", response, cancellationToken).ConfigureAwait(false));
        }

        var bunnyVideo = await response.Content.ReadFromJsonAsync<BunnyVideoResponse>(cancellationToken).ConfigureAwait(false);
        if (bunnyVideo is null)
        {
            return Result.Failure<VideoStatus>(new DomainError("video.provider_error", "Bunny Stream returned an invalid response."));
        }

        var status = MapBunnyStatus(bunnyVideo.Status);
        var duration = bunnyVideo.Length > 0 ? TimeSpan.FromSeconds(bunnyVideo.Length) : (TimeSpan?)null;

        return Result.Success(new VideoStatus(bunnyVideo.Guid, status, duration));
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteVideoAsync(string providerVideoId, CancellationToken cancellationToken)
    {
        var url = $"{BunnyApiBaseUrl}/library/{_options.LibraryId}/videos/{providerVideoId}";

        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, url),
            cancellationToken).ConfigureAwait(false);

        // Bunny returns 200 on successful delete. 404 is also acceptable (already deleted / doesn't exist).
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Success();
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure(await MapErrorAsync("DeleteVideo", response, cancellationToken).ConfigureAwait(false));
        }

        return Result.Success();
    }

    /// <inheritdoc/>
    public Task<Result<SignedPlaybackUrl>> GetSignedPlaybackUrlAsync(
        string providerVideoId,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CdnHostname))
        {
            return Task.FromResult(Result.Failure<SignedPlaybackUrl>(
                new DomainError("video.cdn_not_configured",
                    "VideoProvider:CdnHostname is not configured. Retrieve the real hostname from the Bunny dashboard.")));
        }

        if (string.IsNullOrWhiteSpace(_options.TokenAuthenticationKey))
        {
            return Task.FromResult(Result.Failure<SignedPlaybackUrl>(
                new DomainError("video.token_auth_not_configured",
                    "VideoProvider:TokenAuthenticationKey is not configured. Enable Token Authentication on the Pull Zone and set the key.")));
        }

        var expiresAt = DateTime.UtcNow.Add(timeToLive);
        var expirationTimestamp = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();

        var signedUrl = GenerateSignedPlaybackUrl(providerVideoId, expirationTimestamp);

        return Task.FromResult(Result.Success(new SignedPlaybackUrl(signedUrl, expiresAt)));
    }

    /// <summary>
    /// Generates a token-authenticated playback URL for Bunny Stream.
    /// <para>
    /// Algorithm: <c>SHA256(TokenAuthenticationKey + VideoPath + ExpirationTimestamp)</c> converted
    /// to lowercase hex. The result is appended as <c>?token={hash}&amp;expires={timestamp}</c>.
    /// </para>
    /// <para>
    /// This method is intentionally <c>internal</c> so unit tests can verify the signing logic
    /// directly without needing HTTP mocks.
    /// </para>
    /// </summary>
    internal string GenerateSignedPlaybackUrl(string providerVideoId, long expirationTimestamp)
    {
        // Bunny playback URL path: /{libraryId}/{videoId}/playlist.m3u8
        var videoPath = $"/{_options.LibraryId}/{providerVideoId}/playlist.m3u8";

        var hashInput = $"{_options.TokenAuthenticationKey}{videoPath}{expirationTimestamp}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        var token = Convert.ToHexStringLower(hashBytes);

        return $"https://{_options.CdnHostname}{videoPath}?token={token}&expires={expirationTimestamp}";
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.TryAddWithoutValidation("AccessKey", _options.ApiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// Sends an HTTP request with manual exponential-backoff retry for transient failures (5xx and
    /// 429 Too Many Requests). Non-retryable status codes (4xx other than 429) are returned immediately.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        HttpResponseMessage? lastResponse = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = RetryDelays[Math.Min(attempt - 1, RetryDelays.Length - 1)];
                _logger.LogWarning("Bunny Stream API request failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms.",
                    attempt, MaxRetries + 1, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            // HttpRequestMessage can only be sent once, so create a fresh one per attempt.
            using var request = requestFactory();
            try
            {
                lastResponse = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (IsTransient(lastResponse.StatusCode))
                {
                    continue;
                }

                return lastResponse;
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex, "Bunny Stream API request threw HttpRequestException (attempt {Attempt}/{MaxRetries}).",
                    attempt + 1, MaxRetries + 1);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetries)
            {
                // Timeout (not user cancellation).
                _logger.LogWarning(ex, "Bunny Stream API request timed out (attempt {Attempt}/{MaxRetries}).",
                    attempt + 1, MaxRetries + 1);
            }
        }

        // If we exhausted retries, return the last response we got (caller handles error status).
        return lastResponse ?? throw new InvalidOperationException("No response received from Bunny Stream API after all retry attempts.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private async Task<DomainError> MapErrorAsync(string operation, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogError("Bunny Stream API error during {Operation}: HTTP {StatusCode}. Response: {Body}",
            operation, (int)response.StatusCode, body);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new DomainError("video.unauthorized", "Bunny Stream API key is invalid or expired."),
            HttpStatusCode.Forbidden => new DomainError("video.forbidden", "Bunny Stream API key lacks permission for this operation."),
            HttpStatusCode.NotFound => DomainError.NotFound("The requested video was not found in Bunny Stream."),
            _ => new DomainError("video.provider_error", $"Bunny Stream API returned HTTP {(int)response.StatusCode} during {operation}.")
        };
    }

    private static VideoProcessingStatus MapBunnyStatus(int bunnyStatus) => bunnyStatus switch
    {
        BunnyVideoStatus.Created or BunnyVideoStatus.Uploading => VideoProcessingStatus.Uploading,
        BunnyVideoStatus.Processing or BunnyVideoStatus.Transcoding => VideoProcessingStatus.Processing,
        BunnyVideoStatus.Finished => VideoProcessingStatus.Ready,
        BunnyVideoStatus.Error or BunnyVideoStatus.UploadFailed => VideoProcessingStatus.Failed,
        _ => VideoProcessingStatus.Processing, // Unknown status → treat as still processing.
    };
}
