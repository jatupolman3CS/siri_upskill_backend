using Microsoft.Extensions.Configuration;

namespace Siri.Modules.Learning;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> (<c>Learning:Certificates</c>).
/// <para>
/// <see cref="PublicBaseUrl"/> is the public frontend origin the QR code on a certificate PDF points at
/// (<c>{PublicBaseUrl}/certificates/verify/{verifyCode}</c>). When <c>Learning:Certificates:PublicBaseUrl</c>
/// is not set it falls back to the site-wide <c>Seo:PublicBaseUrl</c> (the same public origin), so there is a
/// single place to configure it. Real data only: there is no built-in domain — while the effective value is
/// blank / not an absolute http(s) URL, generating a certificate PDF fails with
/// <c>certificate.public_url_not_configured</c> (HTTP 503) instead of printing a QR that leads to a site
/// nobody owns; <c>ProductionConfigurationGuard</c> requires a real <c>https://</c> origin in Production.
/// </para>
/// </summary>
public sealed class CertificateOptions
{
    public const string SectionName = "Learning:Certificates";

    /// <summary>The site-wide public origin used when <see cref="PublicBaseUrl"/> is not set explicitly.</summary>
    public const string FallbackConfigurationKey = "Seo:PublicBaseUrl";

    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>The explicitly configured value, or the site-wide <c>Seo:PublicBaseUrl</c> when blank.</summary>
    public static string ResolvePublicBaseUrl(string? configured, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return string.IsNullOrWhiteSpace(configured)
            ? configuration[FallbackConfigurationKey] ?? string.Empty
            : configured;
    }

    /// <summary>The origin without a trailing slash, or <c>null</c> when it is not a usable absolute
    /// http(s) URL.</summary>
    public string? GetNormalizedPublicBaseUrl()
    {
        var value = PublicBaseUrl?.Trim();
        if (string.IsNullOrEmpty(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return value.TrimEnd('/');
    }
}
