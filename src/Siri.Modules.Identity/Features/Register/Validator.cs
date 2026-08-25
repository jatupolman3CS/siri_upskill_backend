using FluentValidation;

namespace Siri.Modules.Identity.Features.Register;

/// <summary>
/// Validates <see cref="RegisterCommand"/> — format/length/policy checks only. Deliberately never
/// checks "is this email already registered" here: that is a business decision the handler makes,
/// and folding it into validation would create a distinguishable-from-success 400 response for an
/// already-registered email, defeating the anti-enumeration behavior the handler is specifically
/// built to guarantee (see <c>Handler.cs</c>'s doc comment).
/// <para>
/// <b>Password policy</b> (backend.md/security.md; modern OWASP guidance — length over forced
/// character-class complexity, e.g. NIST SP 800-63B):
/// <list type="bullet">
/// <item>Minimum <see cref="MinPasswordLength"/> (10) characters. No forced mix of upper/lower/digit/
/// symbol — composition rules push people toward predictable substitutions ("Password1!") that do
/// not actually add entropy, while a plain length floor does, and is far less likely to make a real
/// user pick something they'll immediately forget or write down.</item>
/// <item>Rejects a small, hardcoded denylist of the most obvious weak-but-long passwords — the
/// "technically passes the length bar but everyone tries this first" category (see
/// <see cref="DisallowedPasswords"/>). This is explicitly <b>not</b> a breached-password-list check
/// (no HIBP/similar external API call) — that is out of scope for this task per the task
/// instructions; a real implementation would call an external breached-password API/dataset here.</item>
/// <item>Maximum <see cref="MaxPasswordLength"/> (128) — a sanity cap against pathological input, not
/// a security control (ASP.NET Core Identity's PBKDF2 hasher handles long inputs fine; this just
/// bounds request size / hashing cost).</item>
/// </list>
/// </para>
/// </summary>
public sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 128;
    public const int MinDisplayNameLength = 2;
    public const int MaxDisplayNameLength = 200; // matches Users.DisplayName's column width (UserConfiguration)

    /// <summary>
    /// Small, hardcoded denylist of common weak passwords that are still long enough to otherwise
    /// pass <see cref="MinPasswordLength"/> — every entry here is deliberately &gt;= 10 characters, or
    /// it would already be rejected by the length rule and wouldn't need to be listed. Case-insensitive.
    /// </summary>
    internal static readonly IReadOnlySet<string> DisallowedPasswords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password123",
        "password1234",
        "1234567890",
        "12345678900",
        "0123456789",
        "qwertyuiop123",
        "letmein12345",
        "iloveyou1234",
        "welcome12345",
        "administrator",
        "changeme123",
        "trustno1234",
        "dragon123456",
        "monkey123456",
        "football1234",
        "baseball1234",
    };

    public RegisterValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty()
            .MaximumLength(256) // matches Users.Email's column width (UserConfiguration)
            .EmailAddress();

        RuleFor(c => c.Password)
            .NotEmpty()
            .MinimumLength(MinPasswordLength)
                .WithMessage($"รหัสผ่านต้องมีความยาวอย่างน้อย {MinPasswordLength} ตัวอักษร")
            .MaximumLength(MaxPasswordLength)
            .Must(password => !DisallowedPasswords.Contains(password))
                .WithMessage("รหัสผ่านนี้พบได้บ่อยเกินไปและไม่ปลอดภัย กรุณาเลือกรหัสผ่านอื่น");

        RuleFor(c => c.DisplayName)
            .NotEmpty()
            .MinimumLength(MinDisplayNameLength)
            .MaximumLength(MaxDisplayNameLength);
    }
}
