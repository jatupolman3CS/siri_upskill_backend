using FluentValidation;

namespace Siri.Modules.Identity.Features.AnonymizeAccount;

public sealed class AnonymizeAccountValidator : AbstractValidator<AnonymizeAccountCommand>
{
    public AnonymizeAccountValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");

        RuleFor(x => x.Confirmation)
            .NotEmpty()
            .Must(c => string.Equals(c, "DELETE", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(c, "CONFIRM", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Confirmation text must be 'DELETE' or 'CONFIRM'.");
    }
}
