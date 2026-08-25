using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.ReorderCategories;

/// <summary>
/// Applies a new <c>SortOrder</c> to every sibling under one parent at once.
/// <para>
/// <b>Never trusts a client-claimed parent</b> — the request only carries category ids + their new
/// sort values, no parent id. The handler resolves "which parent" itself from the categories it loads,
/// and rejects the batch outright if those categories don't all currently share exactly one
/// <see cref="Domain.Category.ParentId"/> (<see cref="DomainError.Validation"/>).
/// </para>
/// <para>
/// <b>Requires the full sibling set, rejects partial batches</b> — after resolving the shared parent,
/// the handler loads that parent's complete current sibling list from the database and requires it to
/// match the submitted set exactly (same ids, nothing missing, nothing extra). Applying a partial
/// reorder would leave the siblings not mentioned in the batch holding stale (and potentially
/// duplicate) <c>SortOrder</c> values relative to the ones that did get updated.
/// </para>
/// </summary>
public sealed class ReorderCategoriesHandler(AppDbContext dbContext, CategoryTreeCache cache)
{
    public async Task<Result> HandleAsync(ReorderCategoriesCommand command, CancellationToken cancellationToken)
    {
        var requestedIds = command.Items.Select(i => i.CategoryId).ToHashSet();

        var categories = await dbContext.Categories()
            .Where(c => requestedIds.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (categories.Count != requestedIds.Count)
        {
            return Result.Failure(DomainError.NotFound("พบหมวดหมู่บางรายการที่ไม่มีอยู่จริง"));
        }

        var parentIds = categories.Select(c => c.ParentId).Distinct().ToList();
        if (parentIds.Count != 1)
        {
            return Result.Failure(DomainError.Validation("การจัดลำดับต้องเป็นหมวดหมู่ระดับเดียวกัน (พ่อแม่เดียวกัน) เท่านั้น"));
        }

        var parentId = parentIds[0];

        var actualSiblingIds = await dbContext.Categories()
            .AsNoTracking()
            .Where(c => c.ParentId == parentId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!actualSiblingIds.ToHashSet().SetEquals(requestedIds))
        {
            return Result.Failure(
                DomainError.Validation("ต้องระบุหมวดหมู่ย่อยทุกรายการภายใต้พ่อแม่เดียวกันให้ครบ ห้ามส่งบางส่วน"));
        }

        var sortOrderByCategoryId = command.Items.ToDictionary(i => i.CategoryId, i => i.SortOrder);
        foreach (var category in categories)
        {
            category.Reorder(sortOrderByCategoryId[category.Id]);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.InvalidateAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
