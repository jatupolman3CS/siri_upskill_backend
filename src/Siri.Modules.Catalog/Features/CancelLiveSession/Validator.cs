using FluentValidation;

namespace Siri.Modules.Catalog.Features.CancelLiveSession;

public sealed class CancelLiveSessionCommandValidator : AbstractValidator<CancelLiveSessionCommand>
{
    public CancelLiveSessionCommandValidator()
    {
        // The reason is mailed to every invited learner, so it must not carry a room link either.
        RuleFor(c => c.Reason)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Cancel reason is required.")
            .MaximumLength(500).WithMessage("Cancel reason cannot exceed 500 characters.")
            .NoMeetingLink();
    }
}
