namespace Siri.Integrations.Video.Bunny;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("VideoProvider"). Options pattern +
/// <c>ValidateOnStart()</c> per backend.md's Configuration section, same shape as
/// <c>JwtOptions</c>/<c>EmailConfirmationOptions</c>/<c>ConcurrentSessionOptions</c>.
/// <para>
/// Nothing here is <c>[Required]</c> at startup on purpose: a Development/QA host without a Bunny
/// account must still boot so every non-video feature works. Instead every operation that needs a
/// setting checks it at call time (<see cref="GetMissingApiSettings"/>,
/// <see cref="GetMissingPlaybackSettings"/>, <see cref="GetMissingWebhookSettings"/>) and answers a
/// <c>video.provider_not_configured</c> failure (HTTP 503) — there is no mock/sample-stream fallback.
/// Production is fail-fast: <c>ProductionConfigurationGuard</c> refuses to start the host while any of
/// these settings is empty or still a placeholder.
/// </para>
/// <para>
/// All real values live in <c>dotnet user-secrets</c> (dev) / env (prod) per security.md — never
/// committed to appsettings*.json.
/// </para>
/// </summary>
public sealed class VideoProviderOptions
{
    public const string SectionName = "VideoProvider";

    /// <summary>The value the old appsettings used for "no library yet".</summary>
    private const string UnsetLibraryId = "000000";

    /// <summary>The Bunny Stream video library ID (numeric string from the Bunny dashboard).</summary>
    public string LibraryId { get; set; } = string.Empty;

    /// <summary>The Bunny Stream API key (full access, used for create/delete/status operations and
    /// to sign TUS upload authorization).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Bunny Stream read-only API key. Bunny signs its webhooks with this key, so it is
    /// also the webhook HMAC secret (<c>BunnyWebhookHandler</c>).</summary>
    public string ReadOnlyApiKey { get; set; } = string.Empty;

    /// <summary>The Bunny pull zone name associated with this library.</summary>
    public string PullZone { get; set; } = string.Empty;

    /// <summary>
    /// The CDN hostname for constructing playback URLs, typically in the form
    /// <c>{pull-zone}.b-cdn.net</c> or a custom CNAME.
    /// </summary>
    public string CdnHostname { get; set; } = string.Empty;

    /// <summary>
    /// Token authentication key for signing playback URLs. Retrieved from Pull Zone → Security in the
    /// Bunny dashboard.
    /// </summary>
    public string TokenAuthenticationKey { get; set; } = string.Empty;

    /// <summary>
    /// True when <paramref name="value"/> carries no real configuration: empty/whitespace, one of the
    /// <c>CHANGE_ME…</c> markers committed in appsettings/.env templates, or anything containing the
    /// word "placeholder". Such values must never be used to call or sign anything — a committed
    /// placeholder is publicly known, so e.g. an HMAC key taken from it would be forgeable.
    /// </summary>
    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);

    /// <summary>Settings (by configuration key name, never value) that Bunny's management API calls
    /// (create/status/delete/TUS upload) need but are missing or placeholders.</summary>
    public IReadOnlyList<string> GetMissingApiSettings()
    {
        var missing = new List<string>();
        if (IsPlaceholder(LibraryId) || LibraryId.Trim() == UnsetLibraryId)
        {
            missing.Add($"{SectionName}:{nameof(LibraryId)}");
        }

        if (IsPlaceholder(ApiKey))
        {
            missing.Add($"{SectionName}:{nameof(ApiKey)}");
        }

        return missing;
    }

    /// <summary>Settings that signed playback URLs need but are missing or placeholders.</summary>
    public IReadOnlyList<string> GetMissingPlaybackSettings()
    {
        var missing = new List<string>();
        if (IsPlaceholder(CdnHostname))
        {
            missing.Add($"{SectionName}:{nameof(CdnHostname)}");
        }

        if (IsPlaceholder(TokenAuthenticationKey))
        {
            missing.Add($"{SectionName}:{nameof(TokenAuthenticationKey)}");
        }

        return missing;
    }

    /// <summary>Settings that webhook signature verification needs but are missing or placeholders.</summary>
    public IReadOnlyList<string> GetMissingWebhookSettings() =>
        IsPlaceholder(ReadOnlyApiKey) ? [$"{SectionName}:{nameof(ReadOnlyApiKey)}"] : [];

    /// <summary>Every setting a production host needs (API + playback + webhook), by key name.</summary>
    public IReadOnlyList<string> GetAllMissingSettings() =>
        [.. GetMissingApiSettings(), .. GetMissingPlaybackSettings(), .. GetMissingWebhookSettings()];
}
