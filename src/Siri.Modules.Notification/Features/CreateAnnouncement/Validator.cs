using FluentValidation;

namespace Siri.Modules.Notification.Features.CreateAnnouncement;

public sealed class CreateAnnouncementValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementValidator()
    {
        RuleFor(c => c.CourseId)
            .NotEmpty().WithMessage("Course ID is required.");

        RuleFor(c => c.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(300).WithMessage("Title cannot exceed 300 characters.");

        RuleFor(c => c.Body)
            .NotEmpty().WithMessage("Body is required.");
    }
}
