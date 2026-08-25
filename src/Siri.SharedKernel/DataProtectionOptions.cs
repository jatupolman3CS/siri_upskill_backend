using System.ComponentModel.DataAnnotations;

namespace Siri.SharedKernel;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("DataProtection"). Options pattern +
/// <c>ValidateOnStart()</c> per backend.md's Configuration section — same shape every other secret-backed
/// Options class in this codebase already uses (e.g. <c>Siri.Integrations.Payment.Stripe.StripeOptions</c>).
/// <para>
/// The real key is stored in <c>dotnet user-secrets</c> (dev) / env (prod) per security.md — never
/// committed. This is the encryption-at-rest key for <see cref="SensitiveDataProtector"/> (bank account
/// numbers, tax IDs); it must never live as a literal in source, or anyone who can read the repository —
/// not just the database — can decrypt every protected record.
/// </para>
/// </summary>
public sealed class DataProtectionOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>Base64-encoded 256-bit (32-byte) AES key. Generate one with e.g.
    /// <c>openssl rand -base64 32</c> and set it via <c>dotnet user-secrets set DataProtection:EncryptionKeyBase64 "..."</c>.
    /// </summary>
    [Required]
    public string EncryptionKeyBase64 { get; set; } = string.Empty;
}
