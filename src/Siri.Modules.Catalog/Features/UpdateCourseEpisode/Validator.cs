using FluentValidation;

namespace Siri.Modules.Catalog.Features.UpdateCourseEpisode;

public sealed class UpdateCourseEpisodeValidator : AbstractValidator<UpdateCourseEpisodeCommand>
{
    public UpdateCourseEpisodeValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("กรุณาระบุชื่อบทเรียน")
            .MaximumLength(200).WithMessage("ชื่อบทเรียนต้องไม่เกิน 200 ตัวอักษร");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("คำอธิบายบทเรียนต้องไม่เกิน 2000 ตัวอักษร");
    }
}
