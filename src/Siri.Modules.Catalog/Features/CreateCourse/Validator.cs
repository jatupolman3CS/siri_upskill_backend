using FluentValidation;

namespace Siri.Modules.Catalog.Features.CreateCourse;

/// <summary>Format-only checks — no DB access (mirrors <c>CreateCategoryValidator</c>'s own doc comment:
/// format here, existence/uniqueness/business rules in the handler).</summary>
public sealed class CreateCourseValidator : AbstractValidator<CreateCourseCommand>
{
    public const int MaxTitleLength = 200; // matches Courses.Title's column width (CourseConfiguration)

    public CreateCourseValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(MaxTitleLength);
        RuleFor(c => c.CategoryId).NotEmpty();
        RuleFor(c => c.Level).IsInEnum();
        RuleFor(c => c.Language).IsInEnum();
        RuleFor(c => c.Price).GreaterThanOrEqualTo(0);
    }
}
