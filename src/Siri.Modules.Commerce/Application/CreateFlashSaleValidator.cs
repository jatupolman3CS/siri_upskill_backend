using FluentValidation;

namespace Siri.Modules.Commerce.Application;

/// <summary>Format-only checks — no DB access (mirrors <c>CreateBundleValidator</c>'s own doc comment:
/// format here, course-existence/price business rules in <see cref="FlashSaleService.CreateAsync"/> once it
/// is implemented).</summary>
public sealed class CreateFlashSaleValidator : AbstractValidator<CreateFlashSaleCommand>
{
    public CreateFlashSaleValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.EndsAtUtc).GreaterThan(c => c.StartsAtUtc).WithMessage("EndsAtUtc ต้องอยู่หลัง StartsAtUtc");
        RuleFor(c => c.Items).NotEmpty();
        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.CourseId).NotEmpty();
            item.RuleFor(i => i.SalePrice).GreaterThanOrEqualTo(0);
        });
    }
}
