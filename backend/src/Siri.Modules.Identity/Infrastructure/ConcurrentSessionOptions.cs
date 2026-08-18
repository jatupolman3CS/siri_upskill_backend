using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Identity:Security"). Options pattern +
/// <c>ValidateOnStart()</c> per backend.md's Configuration section, same shape as
/// <c>JwtOptions</c>/<c>EmailConfirmationOptions</c>.
/// <para>
/// System-wide default for SE-03's concurrent-login limit (security.md: "default
/// MaxConcurrentSessions = 2 (ตั้งค่าได้ระดับ system และ override รายบัญชีได้)"). The per-account
/// override lives on <c>Domain.User.MaxConcurrentSessionsOverride</c> instead — Login's enforcement
/// handler (<c>Features/Login/Handler.cs</c>) resolves the *effective* limit for one login as
/// "<c>user.MaxConcurrentSessionsOverride ?? MaxConcurrentSessions</c>", never hardcoding the value
/// itself outside these two places.
/// </para>
/// </summary>
public sealed class ConcurrentSessionOptions
{
    public const string SectionName = "Identity:Security";

    /// <summary>2 per security.md's literal default. Kept configurable — same reasoning as every
    /// other Options-pattern value in this codebase — rather than a hardcoded constant, since it is
    /// exactly the kind of policy value operators may legitimately want to tune per environment.</summary>
    [Range(1, 100)]
    public int MaxConcurrentSessions { get; set; } = 2;
}
