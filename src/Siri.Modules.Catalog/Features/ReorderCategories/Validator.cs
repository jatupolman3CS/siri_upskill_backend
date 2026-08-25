using FluentValidation;

namespace Siri.Modules.Catalog.Features.ReorderCategories;

/// <summary>Format-only checks — no DB access. Whether the batch actually matches one parent's real,
/// full sibling set is a handler-level check (needs the DB); this only rejects batches that are
/// malformed on their own terms.</summary>
public sealed class ReorderCategoriesValidator : AbstractValidator<ReorderCategoriesCommand>
{
    public ReorderCategoriesValidator()
    {
        RuleFor(c => c.Items).NotEmpty();

        RuleFor(c => c.Items)
            .Must(items => items.Select(i => i.CategoryId).Distinct().Count() == items.Count)
                .WithMessage("รายการหมวดหมู่ซ้ำกัน")
            .When(c => c.Items.Count > 0);

        RuleFor(c => c.Items)
            .Must(items => items.Select(i => i.SortOrder).Distinct().Count() == items.Count)
                .WithMessage("ลำดับซ้ำกัน")
            .When(c => c.Items.Count > 0);
    }
}
