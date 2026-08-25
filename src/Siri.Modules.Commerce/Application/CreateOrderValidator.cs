using FluentValidation;

namespace Siri.Modules.Commerce.Application;

/// <summary>Format-only checks — no DB access (mirrors Siri.Modules.Catalog's <c>CreateCourseValidator</c>
/// doc comment: format here, existence/pricing/promo-code business rules in <see cref="OrderService.CreateAsync"/>
/// once it is implemented).</summary>
public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(c => c.CourseIds).NotEmpty();
        RuleForEach(c => c.CourseIds).NotEmpty();

        RuleFor(c => c.PromoCode)
            .MaximumLength(50).WithMessage("โค้ดส่วนลดต้องไม่เกิน 50 ตัวอักษร");
    }
}
