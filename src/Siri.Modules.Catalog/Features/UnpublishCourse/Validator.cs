using FluentValidation;

namespace Siri.Modules.Catalog.Features.UnpublishCourse;

public sealed class UnpublishCourseValidator : AbstractValidator<UnpublishCourseCommand>
{
    public UnpublishCourseValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("กรุณาระบุเหตุผลในการระงับ/ยกเลิกการเผยแพร่คอร์ส")
            .MaximumLength(1000).WithMessage("เหตุผลต้องมีความยาวไม่เกิน 1000 ตัวอักษร");
    }
}
