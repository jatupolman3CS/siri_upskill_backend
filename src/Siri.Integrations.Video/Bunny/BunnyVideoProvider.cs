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
/// <para>
/// Real data only: when the Bunny settings are missing or still placeholders every operation returns a
/// <c>Result.Failure</c> (HTTP 503 at the API) — there is no mock video id, fake upload URL or public
/// test stream fallback.
/// </para>
/// </summary>
public sealed class BunnyVideoProvider : IVideoProvider
{
    /// <summary>Named <see cref="HttpClient"/> identifier registered via
    /// <see cref="IHttpClientFactory"/>.</summary>
    public const string HttpClientName = "BunnyStream";

    /// <summary>
    /// Named <see cref="HttpClient"/> used only by <see cref="UploadVideoAsync"/>: same service, but <c>Timeout = InfiniteTimeSpan</c> (a
    /// multi-GB upload legitimately runs for a long time; the caller's cancellation token is the bound) and no automatic redirect following (a
    /// consumed request stream cannot be replayed). Registered next to <see cref="HttpClientName"/> by the Media module.
    /// </summary>
    public const string UploadHttpClientName = "BunnyStreamUpload";

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
        if (ApiNotConfigured() is { } notConfigured)
        {
            return Result.Failure<VideoAsset>(notConfigured);
        }

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
    /// <remarks>
    /// <c>PUT /library/{libraryId}/videos/{videoId}</c> with the raw bytes as the body (Bunny "Upload Video"). Never retried: the body is a
    /// forward-only stream that a first attempt consumes, so a retry belongs to the caller, which restarts the whole transfer. Transport failures
    /// (reset, DNS, a timeout that is not the caller's cancellation) come back as <c>video.provider_error</c>; the caller's own cancellation
    /// propagates as <see cref="OperationCanceledException"/>. Without a <paramref name="contentLength"/> Bunny receives chunked data, which its
    /// API may reject — pass the size whenever it is known.
    /// </remarks>
    public async Task<Result> UploadVideoAsync(string providerVideoId, Stream content, long? contentLength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (ApiNotConfigured() is { } notConfigured)
        {
            return Result.Failure(notConfigured);
        }

        if (string.IsNullOrWhiteSpace(providerVideoId) || !IsSafePathSegment(providerVideoId))
        {
            return Result.Failure(DomainError.Validation("The provider video id is missing or malformed."));
        }

        var url = $"{BunnyApiBaseUrl}/library/{_options.LibraryId}/videos/{providerVideoId}";

        using var client = _httpClientFactory.CreateClient(UploadHttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.TryAddWithoutValidation("AccessKey", _options.ApiKey);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        // Disposing the request disposes this content and with it <paramref name="content"/> (documented on the interface: the transfer consumes the stream).
        var body = new StreamContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        if (contentLength is { } length)
        {
            body.Headers.ContentLength = length;
        }

        request.Content = body;

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure(await MapErrorAsync("UploadVideo", response, cancellationToken).ConfigureAwait(false));
            }

            return Result.Success();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or IOException or TaskCanceledException)
        {
            // The framework message of these exceptions never carries the AccessKey header or the body; only the type is logged anyway.
            _logger.LogWarning("Bunny Stream upload failed in transit: {ExceptionType}.", ex.GetType().Name);
            return Result.Failure(new DomainError("video.provider_error", "Bunny Stream upload failed (network)."));
        }
    }

    /// <summary>Provider video ids are GUIDs; anything with a path/query character must never be put into a URL.</summary>
    private static bool IsSafePathSegment(string value) =>
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <inheritdoc/>
    public Task<Result<VideoUploadUrl>> GetUploadUrlAsync(string providerVideoId, CancellationToken cancellationToken)
    {
        if (ApiNotConfigured() is { } notConfigured)
        {
            return Task.FromResult(Result.Failure<VideoUploadUrl>(notConfigured));
        }

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
        if (ApiNotConfigured() is { } notConfigured)
        {
            return Result.Failure<VideoStatus>(notConfigured);
        }

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
        if (ApiNotConfigured() is { } notConfigured)
        {
            return Result.Failure(notConfigured);
        }

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
        // Signing is a pure local HMAC, so only the playback settings matter (not the API key). Two
        // codes (rather than one) so callers/tests can tell which half is absent; both map to HTTP 503
        // via the "_not_configured" suffix. Which setting is missing is logged, never returned.
        var missing = _options.GetMissingPlaybackSettings();
        if (missing.Count > 0)
        {
            _logger.LogError("Cannot sign a playback URL: missing/placeholder settings {MissingSettings}.", missing);

            var cdnMissing = VideoProviderOptions.IsPlaceholder(_options.CdnHostname);
            return Task.FromResult(Result.Failure<SignedPlaybackUrl>(cdnMissing
                ? new DomainError("video.cdn_not_configured", "Video CDN hostname is not configured.")
                : new DomainError("video.token_auth_not_configured", "Video token authentication key is not configured.")));
        }

        var expiresAt = DateTime.UtcNow.Add(timeToLive);
        var expirationTimestamp = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();

        var signedUrl = GenerateSignedPlaybackUrl(providerVideoId, expirationTimestamp);

        return Task.FromResult(Result.Success(new SignedPlaybackUrl(signedUrl, expiresAt)));
    }

    /// <summary>
    /// Signs the video directory so relative HLS playlist and segment requests retain authorization.
    /// </summary>
    internal string GenerateSignedPlaybackUrl(string providerVideoId, long expirationTimestamp)
    {
        // A Stream CDN hostname already identifies its library; the asset path starts at the video ID.
        var playlistPath = $"/{providerVideoId}/playlist.m3u8";
        var tokenPath = $"/{providerVideoId}/";

        // Bunny Advanced Token Authentication: key is only the HMAC key. The message contains
        // signature path + expiry + sorted, unescaped signing parameters (excluding token/expires).
        // https://bunny.net/docs/cdn/security/token-authentication/advanced
        var message = $"{tokenPath}{expirationTimestamp}token_path={tokenPath}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.TokenAuthenticationKey));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));

        // Base64URL encode (no padding, RFC 4648 §5)
        var base64 = Convert.ToBase64String(hashBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var token = $"HS256-{base64}";

        // HLS resolves child playlists/segments relative to the manifest; a query token would be lost.
        return $"https://{_options.CdnHostname}/bcdn_token={token}&expires={expirationTimestamp}&token_path={Uri.EscapeDataString(tokenPath)}{playlistPath}";
    }

    /// <summary>
    /// Management-API calls (create/status/delete/TUS upload) need a real library id + API key. A
    /// missing or placeholder value is an operator misconfiguration: fail loudly with a 503-mapped
    /// error instead of fabricating a "mock-…" video or a fake upload URL.
    /// </summary>
    private DomainError? ApiNotConfigured()
    {
        var missing = _options.GetMissingApiSettings();
        if (missing.Count == 0)
        {
            return null;
        }

        _logger.LogError("Bunny Stream API call refused: missing/placeholder settings {MissingSettings}.", missing);
        return VideoProviderErrors.ProviderNotConfigured();
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
        BunnyVideoStatus.Created => VideoProcessingStatus.Uploading,
        BunnyVideoStatus.Uploaded or BunnyVideoStatus.Processing or BunnyVideoStatus.Transcoding => VideoProcessingStatus.Processing,
        BunnyVideoStatus.Finished => VideoProcessingStatus.Ready,
        BunnyVideoStatus.Error or BunnyVideoStatus.UploadFailed => VideoProcessingStatus.Failed,
        _ => VideoProcessingStatus.Processing, // Unknown status → treat as still processing.
    };
}
