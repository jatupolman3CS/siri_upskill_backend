using FluentValidation;

namespace Siri.Modules.Catalog.Features.UpdateLiveSession;

public sealed class UpdateLiveSessionCommandValidator : AbstractValidator<UpdateLiveSessionCommand>
{
    public UpdateLiveSessionCommandValidator()
    {
        // Length first and cascade-stop: the meeting-link scan only ever runs on text that already fits the column.
        RuleFor(c => c.Title)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.")
            .NoMeetingLink();

        RuleFor(c => c.Description)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.")
            .NoMeetingLink();

        RuleFor(c => c.EndsAtUtc)
            .GreaterThan(c => c.StartsAtUtc).WithMessage("endsAtUtc must be after startsAtUtc.");
    }
}
