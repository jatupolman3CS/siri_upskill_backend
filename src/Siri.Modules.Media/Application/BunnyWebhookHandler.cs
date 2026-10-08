using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Video;
using Siri.Integrations.Video.Bunny;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

public sealed record BunnyWebhookPayload(long? VideoLibraryId, string? VideoGuid, int? Status);

public sealed class BunnyWebhookHandler(
    IMediaAssetRepository repository,
    IVideoProvider provider,
    IOptions<VideoProviderOptions> options,
    IClock clock,
    ILogger<BunnyWebhookHandler> logger)
{
    public const int MaximumBodyBytes = 65536;
    public const string InvalidSignature = "media.invalid_webhook_signature";
    public const string ProviderUnavailable = "media.webhook_provider_unavailable";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result> HandleSignedWebhookAsync(
        ReadOnlyMemory<byte> rawBody, string? version, string? algorithm, string? signature,
        CancellationToken cancellationToken)
    {
        // A placeholder/empty signing key is a publicly-known (or empty) HMAC secret: anyone could forge
        // a "valid" signature with it, so no webhook may be accepted until a real key is configured.
        if (options.Value.GetMissingWebhookSettings().Count > 0)
        {
            logger.LogError("Bunny webhook rejected: {Setting} is missing or a placeholder.", $"{VideoProviderOptions.SectionName}:{nameof(VideoProviderOptions.ReadOnlyApiKey)}");
            return Result.Failure(VideoProviderErrors.ProviderNotConfigured());
        }

        // https://bunny.net/docs/stream/webhooks: authenticate exact bytes before JSON parsing.
        if (rawBody.Length > MaximumBodyBytes || version != "v1" || algorithm != "hmac-sha256" ||
            signature is not { Length: 64 } ||
            signature.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            return Result.Failure(new DomainError(InvalidSignature, "Invalid Bunny webhook signature."));
        }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.ReadOnlyApiKey), rawBody.Span);
        if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature)))
        {
            return Result.Failure(new DomainError(InvalidSignature, "Invalid Bunny webhook signature."));
        }

        BunnyWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<BunnyWebhookPayload>(rawBody.Span, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Failure(DomainError.Validation("Invalid Bunny webhook JSON."));
        }
        if (payload is null || string.IsNullOrWhiteSpace(payload.VideoGuid) || payload.Status is null)
        {
            return Result.Failure(DomainError.Validation("VideoGuid and Status are required."));
        }
        if (!long.TryParse(options.Value.LibraryId, out var libraryId) || payload.VideoLibraryId != libraryId)
        {
            return Result.Failure(DomainError.Forbidden("Unexpected Bunny video library."));
        }
        var asset = await repository.GetByProviderAssetIdAsync(payload.VideoGuid, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return Result.Failure(DomainError.NotFound("Video asset was not found."));
        }
        if (asset.STATUS is not (MediaAssetStatus.Uploading or MediaAssetStatus.Processing))
        {
            return Result.Success(); // Duplicate/delayed callbacks cannot regress a terminal state.
        }

        // Callback codes differ from REST codes (3=finished, 6=upload started, 8=upload failed).
        // The provider's current state and duration are authoritative; callbacks are only hints.
        var status = await provider.GetStatusAsync(asset.PROVIDER_ASSET_ID, cancellationToken).ConfigureAwait(false);
        if (status.IsFailure)
        {
            logger.LogWarning("Bunny webhook status refresh failed for asset {AssetId}.", asset.MEDIA_ASSET_ID);
            return Result.Failure(new DomainError(ProviderUnavailable, "Video status is temporarily unavailable."));
        }
        if (MediaAssetStatusUpdater.Apply(asset, status.Value, clock))
        {
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return Result.Success();
    }
}
