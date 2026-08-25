using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Seo"). Options pattern +
/// <c>ValidateOnStart()</c> per backend.md's Configuration section, same shape as
/// <c>Siri.Modules.Identity.Infrastructure.EmailConfirmationOptions</c>.
/// <para>
/// <see cref="PublicBaseUrl"/> is the public-facing frontend origin sitemap.xml/robots.txt URLs are
/// built from — no real production domain has been decided yet (docs/DECISIONS.md's Q3 follow-up notes
/// this same gap for CORS), so this is a placeholder pointing at local dev until one exists — the same
/// "must become the real production origin before launch" situation
/// <c>EmailConfirmationOptions</c>/<c>PasswordResetOptions</c> are already in.
/// </para>
/// </summary>
public sealed class SeoOptions
{
    public const string SectionName = "Seo";

    [Required]
    public string PublicBaseUrl { get; set; } = string.Empty;
}
