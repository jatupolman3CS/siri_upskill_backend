using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Identity:Jwt"). Options pattern +
/// <c>ValidateOnStart()</c> per backend.md's Configuration section, same shape as
/// <c>EmailConfirmationOptions</c>.
/// <para>
/// <see cref="SigningKey"/> is the one genuinely secret value here (everything else — issuer,
/// audience, lifetime — is non-sensitive shape/policy). appsettings*.json only ever carries an
/// obviously-fake <c>CHANGE_ME_DEV_ONLY_...</c> placeholder (long enough that HS256 accepts it, so
/// local dev/tests can start without extra setup) — the real production key belongs in
/// user-secrets/env/Key Vault per security.md, never committed. <see cref="Siri.Api.Program"/> reads
/// this exact same configuration section (via <c>IConfiguration</c> directly, not this options type,
/// since <c>AddJwtBearer</c>'s <c>TokenValidationParameters</c> need the key before the DI container
/// that would resolve <c>IOptions&lt;JwtOptions&gt;</c> exists) to build the
/// <see cref="Microsoft.IdentityModel.Tokens.SymmetricSecurityKey"/> that validates incoming access
/// tokens — so the same bytes sign and verify every token, by construction, with no separate copy to
/// drift out of sync.
/// </para>
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Identity:Jwt";

    /// <summary>Written into every access token's <c>iss</c> claim and required to match on
    /// validation (Program.cs: <c>TokenValidationParameters.ValidIssuer</c>).</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Written into every access token's <c>aud</c> claim and required to match on
    /// validation (Program.cs: <c>TokenValidationParameters.ValidAudience</c>).</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 signing/verification key, UTF-8 encoded. RFC 7518 requires >= 256 bits
    /// (32 bytes) of key material for HS256 — <see cref="MinLength"/> guards that floor at
    /// application start rather than failing obscurely inside JwtBearer's own key-size check.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>15 minutes per security.md ("Access token JWT 15 นาที") — kept configurable (not a
    /// hardcoded constant) since it's exactly the kind of policy value that legitimately differs
    /// between environments during testing/tuning, same reasoning as every other Options-pattern
    /// value in this codebase.</summary>
    [Range(1, 60)]
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
}
