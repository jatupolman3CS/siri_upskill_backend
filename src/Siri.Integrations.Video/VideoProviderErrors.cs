using Siri.SharedKernel;

namespace Siri.Integrations.Video;

/// <summary>
/// Stable <see cref="DomainError"/> codes the video provider integration returns for
/// misconfiguration. Any code ending in <see cref="DomainErrorHttpResults.NotConfiguredCodeSuffix"/>
/// is answered by the API as HTTP 503 ProblemDetails.
/// </summary>
public static class VideoProviderErrors
{
    /// <summary>Bunny credentials (library id / API key) are missing or still placeholders, so no
    /// management-API call can be made. There is deliberately no mock provider fallback.</summary>
    public const string ProviderNotConfiguredCode = "video.provider_not_configured";

    /// <summary>Generic text on purpose: this reaches API clients as the ProblemDetails title, so which
    /// settings are missing is only logged server-side (never echoed to callers).</summary>
    public static DomainError ProviderNotConfigured() =>
        new(ProviderNotConfiguredCode, "Video provider is not configured.");
}
