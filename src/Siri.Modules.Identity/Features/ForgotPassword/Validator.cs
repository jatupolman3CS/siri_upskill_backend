using FluentValidation;

namespace Siri.Modules.Identity.Features.ForgotPassword;

/// <summary>
/// Input-shape validation only — same "never leak account existence through validation" discipline
/// <c>RegisterValidator</c>'s own doc comment lays out: this never checks whether the email is actually
/// registered (that is entirely <c>ForgotPasswordHandler</c>'s concern, and it always returns the same
/// response either way — see its doc comment).
/// </summary>
public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty()
            .MaximumLength(256) // matches Users.Email's column width (UserConfiguration)
            .EmailAddress();
    }
}
