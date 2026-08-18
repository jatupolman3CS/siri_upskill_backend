using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Identity:EmailConfirmation").
/// Options pattern + <c>ValidateOnStart()</c> per backend.md's Configuration section, same shape as
/// <c>Siri.Integrations.Email.SmtpEmailSenderOptions</c>.
/// <para>
/// <see cref="ConfirmEmailUrl"/> is the confirmation page's base URL — the frontend does not have a
/// <c>/confirm-email</c> route yet (this task is backend-only, see the P0-15 task scope), so this is
/// a placeholder pointing at local dev until that page exists. appsettings*.json only ever carries
/// this non-secret placeholder value; it is not a credential, so unlike connection strings it is
/// fine to commit — but it still needs to become the real production frontend origin before launch.
/// </para>
/// </summary>
public sealed class EmailConfirmationOptions
{
    public const string SectionName = "Identity:EmailConfirmation";

    /// <summary>The confirmation link sent to a user is this value plus a <c>?token=</c> query
    /// parameter holding the raw (unhashed) token — see <c>Features/Register/EmailContent.cs</c>.</summary>
    [Required]
    public string ConfirmEmailUrl { get; set; } = string.Empty;
}
