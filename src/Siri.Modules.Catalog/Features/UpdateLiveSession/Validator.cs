using FluentValidation;

namespace Siri.Modules.Catalog.Features.UpdateLiveSession;

public sealed class UpdateLiveSessionCommandValidator : AbstractValidator<UpdateLiveSessionCommand>
{
    public UpdateLiveSessionCommandValidator()
    {
        RuleFor(c => c.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.");

        RuleFor(c => c.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.");

        RuleFor(c => c.EndsAtUtc)
            .GreaterThan(c => c.StartsAtUtc).WithMessage("endsAtUtc must be after startsAtUtc.");
    }
}
