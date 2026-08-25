using FluentValidation;

namespace Siri.Modules.Catalog.Features.ReorderCourseEpisodes;

public sealed class ReorderCourseEpisodesValidator : AbstractValidator<ReorderCourseEpisodesCommand>
{
    public ReorderCourseEpisodesValidator()
    {
        RuleFor(x => x.Items)
            .NotNull().WithMessage("กรุณาระบุรายการจัดลำดับ")
            .Must(items => items is { Count: > 0 }).WithMessage("รายการจัดลำดับต้องมีอย่างน้อย 1 รายการ")
            .Must(items => items.Select(i => i.EpisodeId).Distinct().Count() == items.Count)
            .WithMessage("ห้ามมีรหัสบทเรียนซ้ำกันในชุดจัดลำดับ")
            .Must(items => items.All(i => i.SortOrder >= 0))
            .WithMessage("ลำดับต้องมีค่ามากกว่าหรือเท่ากับ 0");
    }
}
