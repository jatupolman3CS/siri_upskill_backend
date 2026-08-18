using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Identity:PasswordReset"). Options
/// pattern + <c>ValidateOnStart()</c> per backend.md's Configuration section — same shape as
/// <see cref="EmailConfirmationOptions"/>, its closest sibling (both hold nothing but the frontend
/// page's base URL a one-time-token link gets built against).
/// <para>
/// <see cref="ResetPasswordUrl"/> is the "set a new password" page's base URL — same "frontend route
/// does not exist yet, so this is a placeholder pointing at local dev" situation
/// <see cref="EmailConfirmationOptions.ConfirmEmailUrl"/>'s own doc comment already documents; not a
/// secret, so appsettings*.json may carry the real (non-secret) placeholder value directly.
/// </para>
/// </summary>
public sealed class PasswordResetOptions
{
    public const string SectionName = "Identity:PasswordReset";

    /// <summary>The reset-password link sent to a user is this value plus a <c>?token=</c> query
    /// parameter holding the raw (unhashed) token — see <c>Features/ForgotPassword/EmailContent.cs</c>.</summary>
    [Required]
    public string ResetPasswordUrl { get; set; } = string.Empty;
}
