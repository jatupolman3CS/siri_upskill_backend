namespace Siri.Integrations.Storage;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Storage:R2"). Options pattern +
/// <c>ValidateOnStart()</c> per backend.md, same shape as <c>VideoProviderOptions</c>.
/// <para>
/// Nothing here is <c>[Required]</c> at startup on purpose: a Development/QA host without an R2 bucket
/// must still boot so every non-upload feature works. Instead every storage call checks
/// <see cref="GetMissingSettings"/> at call time and answers
/// <see cref="StorageErrors.ProviderNotConfiguredCode"/> (HTTP 503) — there is no local-disk fallback.
/// </para>
/// <para>
/// Real values live in <c>dotnet user-secrets</c> (dev) / environment (prod, <c>Storage__R2__AccountId</c>
/// etc.) per security.md — never committed to appsettings*.json. The access key should be an R2 API token
/// scoped to Object Read &amp; Write on this one bucket only.
/// </para>
/// </summary>
public sealed class R2StorageOptions
{
    public const string SectionName = "Storage:R2";

    /// <summary>Cloudflare account id (hex string shown on the R2 overview page). Used to build the
    /// S3 endpoint <c>https://{AccountId}.r2.cloudflarestorage.com</c>.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>S3-compatible access key id of the R2 API token.</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>S3-compatible secret access key of the R2 API token. Never logged.</summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Name of the private bucket that holds teaching materials.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Optional S3 endpoint override (e.g. the EU-jurisdiction endpoint
    /// <c>https://{AccountId}.eu.r2.cloudflarestorage.com</c>). Empty = the default global endpoint.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// True when <paramref name="value"/> carries no real configuration: empty/whitespace, a
    /// <c>CHANGE_ME…</c> marker, or anything containing the word "placeholder". Same rule as
    /// <c>VideoProviderOptions.IsPlaceholder</c>.
    /// </summary>
    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);

    /// <summary>Settings (by configuration key name, never value) that are missing or placeholders.
    /// <see cref="AccountId"/> is only required while no <see cref="Endpoint"/> override is set.</summary>
    public IReadOnlyList<string> GetMissingSettings()
    {
        var missing = new List<string>();

        if (IsPlaceholder(Endpoint) && IsPlaceholder(AccountId))
        {
            missing.Add($"{SectionName}:{nameof(AccountId)}");
        }

        if (IsPlaceholder(AccessKeyId))
        {
            missing.Add($"{SectionName}:{nameof(AccessKeyId)}");
        }

        if (IsPlaceholder(SecretAccessKey))
        {
            missing.Add($"{SectionName}:{nameof(SecretAccessKey)}");
        }

        if (IsPlaceholder(BucketName))
        {
            missing.Add($"{SectionName}:{nameof(BucketName)}");
        }

        return missing;
    }

    public bool IsConfigured => GetMissingSettings().Count == 0;

    /// <summary>The S3 endpoint to call: the override when set, else the account's default R2 endpoint.</summary>
    public string ResolveServiceUrl() =>
        IsPlaceholder(Endpoint)
            ? $"https://{AccountId.Trim()}.r2.cloudflarestorage.com"
            : Endpoint.Trim().TrimEnd('/');
}
