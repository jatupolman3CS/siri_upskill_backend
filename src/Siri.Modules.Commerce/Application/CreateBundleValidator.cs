using FluentValidation;

namespace Siri.Modules.Commerce.Application;

/// <summary>Format-only checks — no DB access (mirrors Siri.Modules.Catalog's <c>CreateCourseValidator</c>
/// doc comment: format here, slug uniqueness/course-existence business rules in
/// <see cref="BundleService.CreateAsync"/> once it is implemented).</summary>
public sealed class CreateBundleValidator : AbstractValidator<CreateBundleCommand>
{
    public CreateBundleValidator()
    {
        RuleFor(c => c.Slug).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(4000);
        RuleFor(c => c.Price).GreaterThanOrEqualTo(0);
        RuleFor(c => c.CourseIds).NotEmpty();
        RuleForEach(c => c.CourseIds).NotEmpty();
    }
}
