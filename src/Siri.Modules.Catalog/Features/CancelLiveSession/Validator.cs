using FluentValidation;

namespace Siri.Modules.Catalog.Features.CancelLiveSession;

public sealed class CancelLiveSessionCommandValidator : AbstractValidator<CancelLiveSessionCommand>
{
    public CancelLiveSessionCommandValidator()
    {
        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("Cancel reason is required.")
            .MaximumLength(500).WithMessage("Cancel reason cannot exceed 500 characters.");
    }
}
