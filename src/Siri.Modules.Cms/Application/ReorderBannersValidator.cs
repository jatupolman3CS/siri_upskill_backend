using FluentValidation;

namespace Siri.Modules.Cms.Application;

/// <summary>Format-only checks — no DB access. Whether the batch actually matches one placement's real,
/// full sibling set is a future handler-level check (needs the DB); this only rejects batches that are
/// malformed on their own terms. Mirrors
/// <c>Siri.Modules.Catalog.Features.ReorderCategories.ReorderCategoriesValidator</c> exactly.</summary>
public sealed class ReorderBannersValidator : AbstractValidator<ReorderBannersCommand>
{
    public ReorderBannersValidator()
    {
        RuleFor(c => c.Items).NotEmpty();

        RuleFor(c => c.Items)
            .Must(items => items.Select(i => i.BannerId).Distinct().Count() == items.Count)
                .WithMessage("รายการแบนเนอร์ซ้ำกัน")
            .When(c => c.Items.Count > 0);

        RuleFor(c => c.Items)
            .Must(items => items.Select(i => i.SortOrder).Distinct().Count() == items.Count)
                .WithMessage("ลำดับซ้ำกัน")
            .When(c => c.Items.Count > 0);
    }
}
