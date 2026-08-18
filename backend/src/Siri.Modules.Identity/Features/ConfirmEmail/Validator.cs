using FluentValidation;

namespace Siri.Modules.Identity.Features.ConfirmEmail;

/// <summary>
/// Input-shape validation only — never a lookup against stored tokens. Whether a given token exists,
/// is expired, or was already consumed is entirely the handler's concern, and folding any of that
/// into "validation" would create a response distinguishable from the generic invalid/expired error
/// the handler always returns (see <c>Handler.cs</c>'s doc comment on the anti-enumeration parallel).
/// </summary>
public sealed class ConfirmEmailValidator : AbstractValidator<ConfirmEmailCommand>
{
    /// <summary>Generous headroom over the ~64-char hex raw token this codebase actually issues
    /// (<c>SecurityTokenGenerator</c>) — just a sanity cap against pathological request bodies, not a
    /// meaningful security boundary.</summary>
    private const int MaxTokenLength = 512;

    public ConfirmEmailValidator()
    {
        RuleFor(c => c.Token)
            .NotEmpty()
            .MaximumLength(MaxTokenLength);
    }
}
