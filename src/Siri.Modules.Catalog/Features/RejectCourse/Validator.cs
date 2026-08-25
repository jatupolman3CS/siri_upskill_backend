using FluentValidation;

namespace Siri.Modules.Catalog.Features.RejectCourse;

/// <summary>Format-only checks — no DB access (mirrors <c>CreateCategoryValidator</c>'s own doc comment:
/// format here, existence/workflow-stage rules in the handler).</summary>
public sealed class RejectCourseValidator : AbstractValidator<RejectCourseCommand>
{
    public const int MaxReasonLength = 1000; // matches Courses.RejectionReason's column width (CourseConfiguration)

    public RejectCourseValidator()
    {
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(MaxReasonLength);
    }
}
