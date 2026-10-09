using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Siri.Modules.Catalog.Infrastructure.Search;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Meilisearch") — environment variables
/// <c>Meilisearch__Url</c>, <c>Meilisearch__ApiKey</c>, <c>Meilisearch__DocumentsIndexUid</c>. Options pattern +
/// <c>ValidateOnStart()</c> per backend.md's Configuration section.
/// <para>
/// Meilisearch is the primary text-matching engine for the public course search (course title / subtitle /
/// description / category name and the <b>instructor's name</b>). It is deliberately an <em>optional</em> dependency:
/// when <see cref="IsActive"/> is false (no URL, no real API key, or <see cref="Enabled"/> switched off) the search
/// transparently uses the PostgreSQL <c>pg_trgm</c> path that existed before, so a missing or unreachable Meilisearch
/// never takes the catalog down. <see cref="ApiKey"/> is a secret — environment / <c>dotnet user-secrets</c> only,
/// never a committed file (security.md).
/// </para>
/// </summary>
public sealed class MeilisearchOptions
{
    public const string SectionName = "Meilisearch";

    /// <summary>Marker of an unfilled secret (same convention as <c>Identity:Jwt:SigningKey</c>'s CHANGE_ME placeholder).</summary>
    public const string PlaceholderMarker = "CHANGE_ME";

    /// <summary>Kill switch: false forces the database fallback even when everything else is configured.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base URL of the Meilisearch HTTP API, e.g. <c>http://host:7700</c>. Empty = feature off.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Bearer key. Needs search + documents.add/get/delete + indexes.create/get + settings.update + tasks.get
    /// (a master key, or one key created with those actions). Empty or a <c>CHANGE_ME</c> placeholder = feature off.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Uid of the shared documents index. Course documents live in it with <c>type = "course"</c>, so other
    /// document kinds (blog posts, ...) can share the index later without a new one. Meilisearch allows
    /// letters, digits, <c>-</c> and <c>_</c> only.</summary>
    [Required]
    [RegularExpression("^[A-Za-z0-9_-]{1,400}$", ErrorMessage = "DocumentsIndexUid may contain only letters, digits, '-' and '_' (max 400).")]
    public string DocumentsIndexUid { get; set; } = "siriupskill_documents";

    /// <summary>Budget for one search call; past it the request falls back to the database instead of making the visitor wait.</summary>
    [Range(100, 30_000)]
    public int SearchTimeoutMilliseconds { get; set; } = 2_000;

    /// <summary>Budget for every other call (indexing, settings, stats).</summary>
    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>Documents sent per indexing request.</summary>
    [Range(1, 10_000)]
    public int IndexBatchSize { get; set; } = 500;

    /// <summary>Most hits one search can return — the pagination ceiling of the index (<c>pagination.maxTotalHits</c>); the database
    /// then applies filters/sort/paging to this candidate set.</summary>
    [Range(10, 10_000)]
    public int MaxSearchHits { get; set; } = 1_000;

    /// <summary>How long a bulk indexing call waits for Meilisearch to finish its (asynchronous) task before moving on.</summary>
    [Range(1, 600)]
    public int TaskWaitTimeoutSeconds { get; set; } = 60;

    /// <summary>Consecutive failures after which searches skip Meilisearch for <see cref="CircuitOpenSeconds"/> (so an outage costs one
    /// timeout per window, not one per visitor).</summary>
    [Range(1, 100)]
    public int CircuitBreakerFailureThreshold { get; set; } = 3;

    [Range(1, 3_600)]
    public int CircuitOpenSeconds { get; set; } = 30;

    /// <summary>True only when searches/indexing may actually be sent to Meilisearch.</summary>
    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(Url) && HasRealApiKey;

    public bool HasRealApiKey =>
        !string.IsNullOrWhiteSpace(ApiKey) && !ApiKey.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Why <see cref="IsActive"/> is false — for the single startup log line; never contains the key.</summary>
    public string? InactiveReason =>
        !Enabled ? "Meilisearch:Enabled is false"
        : string.IsNullOrWhiteSpace(Url) ? "Meilisearch:Url is not set"
        : !HasRealApiKey ? "Meilisearch:ApiKey is empty or still a CHANGE_ME placeholder"
        : null;

    /// <summary>The base address with exactly one trailing slash (so relative request paths resolve under it), or null when unset/malformed.</summary>
    public Uri? GetBaseAddress()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            return null;
        }

        var trimmed = Url.Trim().TrimEnd('/') + "/";
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }
}

/// <summary>Cross-field rules <see cref="ValidationAttribute"/>s cannot express. A malformed URL is a boot-time failure (a typo must not
/// silently disable search); an absent URL or key is not (that simply means the database fallback is used).</summary>
public sealed partial class MeilisearchOptionsValidator : IValidateOptions<MeilisearchOptions>
{
    public ValidateOptionsResult Validate(string? name, MeilisearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.Url) && options.GetBaseAddress() is null)
        {
            return ValidateOptionsResult.Fail($"{MeilisearchOptions.SectionName}:Url must be an absolute http:// or https:// URL.");
        }

        if (string.IsNullOrWhiteSpace(options.DocumentsIndexUid) || !IndexUidPattern().IsMatch(options.DocumentsIndexUid))
        {
            return ValidateOptionsResult.Fail(
                $"{MeilisearchOptions.SectionName}:DocumentsIndexUid may contain only letters, digits, '-' and '_' (max 400 characters).");
        }

        return ValidateOptionsResult.Success;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,400}$")]
    private static partial Regex IndexUidPattern();
}
