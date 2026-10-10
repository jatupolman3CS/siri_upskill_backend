using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.SharedKernel;

namespace Siri.Integrations.Storage;

/// <summary>
/// <see cref="IFileStorage"/> over Cloudflare R2 using its S3-compatible API (<c>AWSSDK.S3</c>).
/// <list type="bullet">
/// <item>One long-lived <see cref="AmazonS3Client"/> (thread-safe, pools its own connections), created
/// lazily so a host without R2 settings still boots and just answers
/// <see cref="StorageErrors.ProviderNotConfiguredCode"/>.</item>
/// <item>R2 does not implement the SDK's default "streaming SigV4" upload nor the v4 default integrity
/// checksums, so the client is configured for <c>WHEN_REQUIRED</c> checksums and uploads with
/// <c>DisablePayloadSigning</c> (TLS already protects the payload; Cloudflare's own .NET example does the
/// same).</item>
/// <item>Path-style addressing, region <c>auto</c> — what R2 documents for S3 clients.</item>
/// <item>Provider error text is logged, never returned to API callers.</item>
/// </list>
/// </summary>
public sealed class R2FileStorage : IFileStorage, IDisposable
{
    /// <summary>Longest key the catalog's <c>STORAGE_KEY</c> column holds; also keeps keys well under S3's 1024-byte limit.</summary>
    public const int MaxKeyLength = 500;

    private readonly R2StorageOptions _options;
    private readonly ILogger<R2FileStorage> _logger;
    private readonly HttpClientFactory? _httpClientFactory;
    private readonly Lazy<IAmazonS3> _client;

    public R2FileStorage(IOptions<R2StorageOptions> options, ILogger<R2FileStorage> logger)
        : this(options, logger, httpClientFactory: null)
    {
    }

    /// <summary>Test seam: lets a unit test intercept the HTTP traffic without a network or a real bucket.</summary>
    internal R2FileStorage(
        IOptions<R2StorageOptions> options,
        ILogger<R2FileStorage> logger,
        HttpClientFactory? httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _client = new Lazy<IAmazonS3>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<Result<StoredFile>> UploadAsync(
        string key,
        Stream content,
        long contentLength,
        string contentType,
        string? downloadFileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (GuardConfigured() is { } notConfigured)
        {
            return Result.Failure<StoredFile>(notConfigured);
        }

        if (!IsValidKey(key))
        {
            return Result.Failure<StoredFile>(StorageErrors.InvalidKey());
        }

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName.Trim(),
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true,
        };
        request.Headers.ContentLength = contentLength;

        if (!string.IsNullOrWhiteSpace(downloadFileName))
        {
            request.Headers.ContentDisposition = ContentDispositionHeader.BuildAttachment(downloadFileName);
        }

        try
        {
            await _client.Value.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
            return Result.Success(new StoredFile(key, contentLength));
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException)
        {
            _logger.LogError(ex, "R2 upload failed for key {StorageKey}.", key);
            return Result.Failure<StoredFile>(StorageErrors.OperationFailed());
        }
    }

    public Task<Result<string>> GetSignedUrlAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        if (GuardConfigured() is { } notConfigured)
        {
            return Task.FromResult(Result.Failure<string>(notConfigured));
        }

        if (!IsValidKey(key) || timeToLive <= TimeSpan.Zero || timeToLive > TimeSpan.FromDays(7))
        {
            return Task.FromResult(Result.Failure<string>(StorageErrors.InvalidKey()));
        }

        try
        {
            // Pure local computation (HMAC over the request) — no network call, so no cancellation to honour.
            var url = _client.Value.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName.Trim(),
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(timeToLive),
                Protocol = Protocol.HTTPS,
            });

            return Task.FromResult(Result.Success(url));
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException)
        {
            _logger.LogError(ex, "R2 could not sign a download URL for key {StorageKey}.", key);
            return Task.FromResult(Result.Failure<string>(StorageErrors.OperationFailed()));
        }
    }

    public async Task<Result> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (GuardConfigured() is { } notConfigured)
        {
            return Result.Failure(notConfigured);
        }

        if (!IsValidKey(key))
        {
            return Result.Failure(StorageErrors.InvalidKey());
        }

        try
        {
            await _client.Value.DeleteObjectAsync(_options.BucketName.Trim(), key, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException)
        {
            _logger.LogError(ex, "R2 delete failed for key {StorageKey}.", key);
            return Result.Failure(StorageErrors.OperationFailed());
        }
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    /// <summary>Keys are always server-built (<c>teaching-materials/...</c>); this is defence in depth against a
    /// caller ever passing something that could escape the prefix or confuse a client.</summary>
    internal static bool IsValidKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.Length <= MaxKeyLength
        && key[0] != '/'
        && !key.Contains('\\')
        && !key.Contains("..", StringComparison.Ordinal)
        && !key.Any(char.IsControl);

    private DomainError? GuardConfigured()
    {
        var missing = _options.GetMissingSettings();
        if (missing.Count == 0)
        {
            return null;
        }

        _logger.LogError("File storage is not configured; missing settings: {MissingSettings}.", string.Join(", ", missing));
        return StorageErrors.ProviderNotConfigured();
    }

    private IAmazonS3 CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = _options.ResolveServiceUrl(),
            AuthenticationRegion = "auto",
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        if (_httpClientFactory is not null)
        {
            config.HttpClientFactory = _httpClientFactory;
        }

        return new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKeyId.Trim(), _options.SecretAccessKey.Trim()),
            config);
    }
}
