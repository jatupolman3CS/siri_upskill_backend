using FluentValidation;

namespace Siri.Modules.Catalog.Features.CreateCategory;

/// <summary>
/// Format-only checks — no DB access (mirrors <c>Siri.Modules.Identity.Features.Register
/// .RegisterValidator</c>'s split: format here, uniqueness/existence/business rules in the handler).
/// </summary>
public sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public const int MaxSlugLength = 100; // matches Categories.Slug's column width (CategoryConfiguration)
    public const int MaxNameLength = 200; // matches Categories.NameTh/NameEn's column width
    public const int MaxIconKeyLength = 100;

    /// <summary>Lowercase ASCII letters/digits, hyphen-separated, no leading/trailing/doubled hyphens —
    /// no Thai→Latin auto-generation for categories (unlike COURSE CRUD's P1-04); the admin types this.</summary>
    private const string SlugPattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

    public CreateCategoryValidator()
    {
        RuleFor(c => c.Slug)
            .NotEmpty()
            .MaximumLength(MaxSlugLength)
            .Matches(SlugPattern)
                .WithMessage("Slug ต้องเป็นตัวอักษรภาษาอังกฤษพิมพ์เล็ก ตัวเลข และขีดกลางเท่านั้น (เช่น web-development)");

        RuleFor(c => c.NameTh).NotEmpty().MaximumLength(MaxNameLength);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(MaxNameLength);
        RuleFor(c => c.IconKey).MaximumLength(MaxIconKeyLength);
    }
}
