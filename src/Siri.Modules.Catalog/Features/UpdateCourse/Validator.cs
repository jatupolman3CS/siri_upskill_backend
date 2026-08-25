using FluentValidation;

namespace Siri.Modules.Catalog.Features.UpdateCourse;

/// <summary>Format-only checks — no DB access (mirrors <c>CreateCategoryValidator</c>'s own doc comment:
/// format here, existence/ownership/business rules in the handler). Max lengths match
/// <c>CourseConfiguration</c>'s column widths exactly.</summary>
public sealed class UpdateCourseValidator : AbstractValidator<UpdateCourseCommand>
{
    public const int MaxTitleLength = 200;
    public const int MaxSubtitleLength = 300;
    public const int MaxDescriptionLength = 4000;
    public const int MaxThumbnailUrlLength = 1000;
    public const int MaxSeoTitleLength = 200;
    public const int MaxSeoDescriptionLength = 500;

    public UpdateCourseValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(MaxTitleLength);
        RuleFor(c => c.Subtitle).MaximumLength(MaxSubtitleLength);
        RuleFor(c => c.Description).MaximumLength(MaxDescriptionLength);
        RuleFor(c => c.CategoryId).NotEmpty();
        RuleFor(c => c.Level).IsInEnum();
        RuleFor(c => c.Language).IsInEnum();
        RuleFor(c => c.ThumbnailUrl).MaximumLength(MaxThumbnailUrlLength);
        RuleFor(c => c.Price).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ComparePrice).GreaterThanOrEqualTo(0).When(c => c.ComparePrice is not null);
        RuleFor(c => c.AccessDurationDays).GreaterThan(0).When(c => c.AccessDurationDays is not null);
        RuleFor(c => c.SeoTitle).MaximumLength(MaxSeoTitleLength);
        RuleFor(c => c.SeoDescription).MaximumLength(MaxSeoDescriptionLength);
    }
}
