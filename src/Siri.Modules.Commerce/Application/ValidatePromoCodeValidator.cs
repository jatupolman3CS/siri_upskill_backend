using FluentValidation;

namespace Siri.Modules.Commerce.Application;

public sealed class ValidatePromoCodeValidator : AbstractValidator<ValidatePromoCodeCommand>
{
    public ValidatePromoCodeValidator()
    {
        RuleFor(c => c.Code)
            .NotEmpty().WithMessage("กรุณาระบุโค้ดส่วนลด")
            .MaximumLength(50).WithMessage("โค้ดส่วนลดต้องไม่เกิน 50 ตัวอักษร");

        RuleFor(c => c.CourseIds)
            .NotEmpty().WithMessage("กรุณาระบุคอร์สเรียนในตะกร้า");

        RuleForEach(c => c.CourseIds)
            .NotEmpty().WithMessage("รหัสคอร์สเรียนไม่ถูกต้อง");
    }
}
