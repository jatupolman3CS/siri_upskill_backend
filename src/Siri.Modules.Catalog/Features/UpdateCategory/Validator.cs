using FluentValidation;
using Siri.Modules.Catalog.Features.CreateCategory;

namespace Siri.Modules.Catalog.Features.UpdateCategory;

/// <summary>Format-only checks, reusing <see cref="CreateCategoryValidator"/>'s constants/pattern
/// directly rather than duplicating the rule (same "reference, don't copy" approach
/// <c>Identity.Features.ResetPassword.ResetPasswordValidator</c> takes with
/// <c>RegisterValidator</c>'s constants).</summary>
public sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategoryCommand>
{
    private const string SlugPattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

    public UpdateCategoryValidator()
    {
        RuleFor(c => c.Slug)
            .NotEmpty()
            .MaximumLength(CreateCategoryValidator.MaxSlugLength)
            .Matches(SlugPattern)
                .WithMessage("Slug ต้องเป็นตัวอักษรภาษาอังกฤษพิมพ์เล็ก ตัวเลข และขีดกลางเท่านั้น (เช่น web-development)");

        RuleFor(c => c.NameTh).NotEmpty().MaximumLength(CreateCategoryValidator.MaxNameLength);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(CreateCategoryValidator.MaxNameLength);
        RuleFor(c => c.IconKey).MaximumLength(CreateCategoryValidator.MaxIconKeyLength);
    }
}
