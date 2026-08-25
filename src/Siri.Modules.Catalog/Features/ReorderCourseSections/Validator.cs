using FluentValidation;

namespace Siri.Modules.Catalog.Features.ReorderCourseSections;

public sealed class ReorderCourseSectionsValidator : AbstractValidator<ReorderCourseSectionsCommand>
{
    public ReorderCourseSectionsValidator()
    {
        RuleFor(x => x.Items)
            .NotNull().WithMessage("กรุณาระบุรายการจัดลำดับ")
            .Must(items => items is { Count: > 0 }).WithMessage("รายการจัดลำดับต้องมีอย่างน้อย 1 รายการ")
            .Must(items => items.Select(i => i.SectionId).Distinct().Count() == items.Count)
            .WithMessage("ห้ามมีรหัสส่วน/บทหลักซ้ำกันในชุดจัดลำดับ")
            .Must(items => items.All(i => i.SortOrder >= 0))
            .WithMessage("ลำดับต้องมีค่ามากกว่าหรือเท่ากับ 0");
    }
}
