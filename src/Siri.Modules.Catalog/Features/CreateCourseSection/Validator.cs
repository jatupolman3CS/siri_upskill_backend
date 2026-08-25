using FluentValidation;

namespace Siri.Modules.Catalog.Features.CreateCourseSection;

public sealed class CreateCourseSectionValidator : AbstractValidator<CreateCourseSectionCommand>
{
    public CreateCourseSectionValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("กรุณาระบุชื่อส่วน/บทหลัก")
            .MaximumLength(200).WithMessage("ชื่อส่วน/บทหลักต้องไม่เกิน 200 ตัวอักษร");
    }
}
