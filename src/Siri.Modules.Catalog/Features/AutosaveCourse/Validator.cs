using FluentValidation;

namespace Siri.Modules.Catalog.Features.AutosaveCourse;

public sealed class AutosaveCourseValidator : AbstractValidator<AutosaveCourseCommand>
{
    public AutosaveCourseValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("กรุณาระบุชื่อคอร์ส")
            .MaximumLength(200).WithMessage("ชื่อคอร์สต้องไม่เกิน 200 ตัวอักษร");

        RuleFor(x => x.Subtitle)
            .MaximumLength(300).WithMessage("คำโปรยต้องไม่เกิน 300 ตัวอักษร");

        RuleFor(x => x.Description)
            .MaximumLength(4000).WithMessage("รายละเอียดต้องไม่เกิน 4000 ตัวอักษร");

        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("กรุณาระบุหมวดหมู่");

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).WithMessage("ราคาต้องไม่ติดลบ");

        RuleFor(x => x.ComparePrice)
            .GreaterThanOrEqualTo(0).When(x => x.ComparePrice.HasValue).WithMessage("ราคาเต็มต้องไม่ติดลบ");

        RuleFor(x => x.AccessDurationDays)
            .GreaterThan(0).When(x => x.AccessDurationDays.HasValue).WithMessage("ระยะเวลาเข้าถึงต้องมากกว่า 0 วัน");

        RuleFor(x => x.SeoTitle)
            .MaximumLength(200).WithMessage("SEO Title ต้องไม่เกิน 200 ตัวอักษร");

        RuleFor(x => x.SeoDescription)
            .MaximumLength(500).WithMessage("SEO Description ต้องไม่เกิน 500 ตัวอักษร");

        RuleFor(x => x.RowVersion)
            .NotNull().NotEmpty().WithMessage("กรุณาระบุ Concurrency Token (RowVersion)");

        When(x => x.Sections != null, () =>
        {
            RuleFor(x => x.Sections)
                .Must(s => s == null || s.Count <= 100)
                .WithMessage("จำนวนส่วน/บทหลักต้องไม่เกิน 100 ส่วน");

            RuleForEach(x => x.Sections).ChildRules(section =>
            {
                section.RuleFor(s => s.Title)
                    .NotEmpty().WithMessage("กรุณาระบุชื่อส่วน/บทหลัก")
                    .MaximumLength(200).WithMessage("ชื่อส่วน/บทหลักต้องไม่เกิน 200 ตัวอักษร");

                section.When(s => s.Episodes != null, () =>
                {
                    section.RuleFor(s => s.Episodes)
                        .Must(e => e == null || e.Count <= 200)
                        .WithMessage("จำนวนบทเรียนในแต่ละส่วนต้องไม่เกิน 200 ตอน");

                    section.RuleForEach(s => s.Episodes).ChildRules(episode =>
                    {
                        episode.RuleFor(e => e.Title)
                            .NotEmpty().WithMessage("กรุณาระบุชื่อบทเรียน")
                            .MaximumLength(200).WithMessage("ชื่อบทเรียนต้องไม่เกิน 200 ตัวอักษร");

                        episode.RuleFor(e => e.Description)
                            .MaximumLength(2000).WithMessage("คำอธิบายบทเรียนต้องไม่เกิน 2000 ตัวอักษร");
                    });
                });
            });
        });
    }
}
