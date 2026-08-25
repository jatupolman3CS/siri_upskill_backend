using System.ComponentModel.DataAnnotations;

namespace Siri.Integrations.Video.Bunny;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("VideoProvider"). Options pattern +
/// <c>ValidateOnStart()</c> per backend.md's Configuration section, same shape as
/// <c>JwtOptions</c>/<c>EmailConfirmationOptions</c>/<c>ConcurrentSessionOptions</c>.
/// <para>
/// Every property except <see cref="CdnHostname"/> is [Required] — the CDN hostname is deliberately
/// optional at startup because the project owner hasn't retrieved the real value from the Bunny dashboard
/// yet (docs/DECISIONS.md Q1 notes the placeholder). Code that needs it (<see cref="BunnyVideoProvider.GetSignedPlaybackUrlAsync"/>)
/// checks at call time and returns a clear <c>Result.Failure</c> instead of silently producing a bad URL.
/// </para>
/// <para>
/// All real values live in <c>dotnet user-secrets</c> (dev) / env (prod) per security.md — never
/// committed to appsettings*.json.
/// </para>
/// </summary>
public sealed class VideoProviderOptions
{
    public const string SectionName = "VideoProvider";

    /// <summary>The Bunny Stream video library ID (numeric string from the Bunny dashboard).</summary>
    [Required]
    public string LibraryId { get; set; } = string.Empty;

    /// <summary>The Bunny Stream API key (full access, used for create/delete/status operations).</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Bunny Stream read-only API key (used where full-access isn't needed).</summary>
    [Required]
    public string ReadOnlyApiKey { get; set; } = string.Empty;

    /// <summary>The Bunny pull zone name associated with this library (used in upload signature
    /// generation).</summary>
    [Required]
    public string PullZone { get; set; } = string.Empty;

    /// <summary>
    /// The CDN hostname for constructing playback URLs, typically in the form
    /// <c>{pull-zone}.b-cdn.net</c> or a custom CNAME. Not [Required] — see this class's own doc
    /// comment for why.
    /// </summary>
    public string CdnHostname { get; set; } = string.Empty;

    /// <summary>
    /// Token authentication key for signing playback URLs. Retrieved from Pull Zone → Security in the
    /// Bunny dashboard. Required once playback URL signing is needed (P2-04).
    /// </summary>
    public string TokenAuthenticationKey { get; set; } = string.Empty;
}
