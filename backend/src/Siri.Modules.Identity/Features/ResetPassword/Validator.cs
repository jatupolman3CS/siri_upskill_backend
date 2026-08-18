using FluentValidation;
using Siri.Modules.Identity.Features.Register;

namespace Siri.Modules.Identity.Features.ResetPassword;

/// <summary>
/// Validates <see cref="ResetPasswordCommand"/> — input *shape* only, same "never fold a stored-token
/// lookup into validation" discipline <c>ConfirmEmailValidator</c>'s own doc comment lays out for
/// <see cref="ResetPasswordCommand.Token"/>'s counterpart there (whether the token exists/is expired/was already consumed is
/// entirely <c>ResetPasswordHandler</c>'s job).
/// <para>
/// <b>Password policy — literally reused from <see cref="RegisterValidator"/>, not reimplemented</b>
/// (task instruction: "reuse Register's exact password policy ... do not invent a different policy";
/// also instructed to "consider whether to literally reuse Register's validator class or duplicate the
/// rules, and justify your choice"). Neither option on offer is a clean fit: <b>(a) reusing
/// <see cref="RegisterValidator"/> itself</b> is not literally possible — FluentValidation validators
/// are strongly typed to one command (<c>AbstractValidator&lt;RegisterCommand&gt;</c>), and this
/// validates a different type (<see cref="ResetPasswordCommand"/>), so there is no way to "just use
/// <see cref="RegisterValidator"/>" without an adapter/wrapper that would itself be new code; <b>(b)
/// copy-pasting the rule values</b> (minimum length, denylist, maximum length) would create two
/// independent copies of the same policy that could silently drift apart the next time either one is
/// tuned — precisely what "do not invent a different policy" is warning against. So this validator
/// instead references <see cref="RegisterValidator.MinPasswordLength"/>,
/// <see cref="RegisterValidator.MaxPasswordLength"/>, and <see cref="RegisterValidator.DisallowedPasswords"/>
/// directly (the last is <c>internal</c>, not <c>public</c>, but both types live in this same assembly,
/// <c>Siri.Modules.Identity</c>, so that is a real reuse of the one canonical rule set, not a second
/// definition of it) — the closest thing to "literally reusing Register's validator" that
/// FluentValidation's per-type design actually allows, verified in
/// <c>tests/Siri.UnitTests/Identity/ResetPasswordValidatorTests.cs</c> rather than merely assumed.
/// </para>
/// </summary>
public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    /// <summary>Same sanity cap/reasoning as <c>ConfirmEmailValidator.MaxTokenLength</c> — generous
    /// headroom over the ~64-char hex raw token this codebase actually issues.</summary>
    private const int MaxTokenLength = 512;

    public ResetPasswordValidator()
    {
        RuleFor(c => c.Token)
            .NotEmpty()
            .MaximumLength(MaxTokenLength);

        RuleFor(c => c.NewPassword)
            .NotEmpty()
            .MinimumLength(RegisterValidator.MinPasswordLength)
                .WithMessage($"รหัสผ่านต้องมีความยาวอย่างน้อย {RegisterValidator.MinPasswordLength} ตัวอักษร")
            .MaximumLength(RegisterValidator.MaxPasswordLength)
            .Must(password => !RegisterValidator.DisallowedPasswords.Contains(password))
                .WithMessage("รหัสผ่านนี้พบได้บ่อยเกินไปและไม่ปลอดภัย กรุณาเลือกรหัสผ่านอื่น");
    }
}
