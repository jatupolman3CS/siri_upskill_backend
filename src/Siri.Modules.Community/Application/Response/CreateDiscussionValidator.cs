using FluentValidation;

namespace Siri.Modules.Community.Application.Response;

/// <summary>Format-only checks — no DB access (mirrors
/// <c>Siri.Modules.Catalog.Features.CreateCourse.CreateCourseValidator</c>'s own doc comment: format
/// here, existence/ownership/business rules in <c>DiscussionService</c>).</summary>
public sealed class CreateDiscussionValidator : AbstractValidator<CreateDiscussionCommand>
{
    public const int MaxBodyLength = 4000; // matches DISCUSSIONS.BODY's column width (DiscussionConfiguration)

    public CreateDiscussionValidator()
    {
        RuleFor(c => c.CourseId).NotEmpty();
        RuleFor(c => c.Body).NotEmpty().MaximumLength(MaxBodyLength);
    }
}
