using FluentValidation;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

/// <summary>Format-only checks — no DB access (mirrors Siri.Modules.Catalog's <c>CreateCourseValidator</c>
/// doc comment: format here, <see cref="PROMO_CODE.CODE"/> uniqueness and
/// <see cref="CreatePromoCodeCommand.ScopeRefId"/> existence in <see cref="PromoCodeService.CreateAsync"/>
/// once it is implemented). The cross-field rules below (percentage cap, scope/scopeRefId pairing) mirror
/// invariants <see cref="PROMO_CODE.Create"/> already enforces in the domain — duplicated here only so a
/// bad request fails fast as a 400 instead of an unhandled <see cref="ArgumentException"/>.</summary>
public sealed class CreatePromoCodeValidator : AbstractValidator<CreatePromoCodeCommand>
{
    public CreatePromoCodeValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(32);
        RuleFor(c => c.DiscountType).IsInEnum();
        RuleFor(c => c.DiscountValue).GreaterThan(0);
        RuleFor(c => c.DiscountValue)
            .LessThanOrEqualTo(100)
            .When(c => c.DiscountType == PromoCodeDiscountType.Percentage)
            .WithMessage("ส่วนลดแบบเปอร์เซ็นต์ต้องไม่เกิน 100");

        RuleFor(c => c.MaxRedemptions).GreaterThan(0);
        RuleFor(c => c.MaxPerUser).GreaterThan(0);
        RuleFor(c => c.MinOrderAmount).GreaterThanOrEqualTo(0);

        RuleFor(c => c.EndsAtUtc).GreaterThan(c => c.StartsAtUtc).WithMessage("EndsAtUtc ต้องอยู่หลัง StartsAtUtc");

        RuleFor(c => c.Scope).IsInEnum();
        RuleFor(c => c.ScopeRefId)
            .NotEmpty()
            .When(c => c.Scope != PromoCodeScope.AllCourses)
            .WithMessage("ScopeRefId จำเป็นต้องระบุ ยกเว้น Scope เป็น AllCourses");
    }
}
