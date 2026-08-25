using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UpdateCategory;

/// <summary>
/// Updates a category's rename/slug/icon/active fields and, when <see cref="UpdateCategoryCommand.ParentId"/>
/// differs from the current value, re-parents it.
/// <para>
/// <b>Cycle prevention</b> — moving a category under one of its own descendants would corrupt the
/// tree (an infinite loop for anything that walks it). <see cref="Category.MoveTo"/> only rejects the
/// trivial "under myself" case (it cannot see the rest of the tree — backend.md: domain stays
/// EF-ignorant); this handler does the general check: load every category once, then walk up from the
/// requested new parent following <c>ParentId</c> links. If that walk ever reaches the category being
/// moved, the move would create a cycle — reject with <see cref="DomainError.Validation"/>. This also
/// catches non-adjacent cases (moving under a grandchild, great-grandchild, ...): walking up from any
/// descendant necessarily passes through every one of its ancestors, including the category being
/// moved, if and only if that category actually is an ancestor of the requested new parent — which is
/// exactly the condition that would create a cycle.
/// </para>
/// </summary>
public sealed class UpdateCategoryHandler(AppDbContext dbContext, CategoryTreeCache cache)
{
    public async Task<Result<UpdateCategoryResponse>> HandleAsync(Guid id, UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        // Loaded tracked (no AsNoTracking): one entry gets mutated below, and the full set doubles as
        // the in-memory graph the cycle walk needs — no second query.
        var categories = await dbContext.Categories().ToListAsync(cancellationToken).ConfigureAwait(false);
        var byId = categories.ToDictionary(c => c.Id);

        if (!byId.TryGetValue(id, out var target))
        {
            return Result.Failure<UpdateCategoryResponse>(DomainError.NotFound("ไม่พบหมวดหมู่นี้"));
        }

        var slugTaken = categories.Any(c =>
            c.Id != id && string.Equals(c.Slug, command.Slug, StringComparison.OrdinalIgnoreCase));

        if (slugTaken)
        {
            return Result.Failure<UpdateCategoryResponse>(DomainError.Conflict($"Slug '{command.Slug}' ถูกใช้แล้ว"));
        }

        if (command.ParentId != target.ParentId)
        {
            if (command.ParentId is { } newParentId)
            {
                if (!byId.TryGetValue(newParentId, out _))
                {
                    return Result.Failure<UpdateCategoryResponse>(DomainError.NotFound("ไม่พบหมวดหมู่แม่ที่ระบุ"));
                }

                var current = (Guid?)newParentId;
                while (current is { } currentId)
                {
                    if (currentId == target.Id)
                    {
                        return Result.Failure<UpdateCategoryResponse>(
                            DomainError.Validation("ไม่สามารถย้ายหมวดหมู่ไปไว้ใต้หมวดหมู่ย่อยของตัวเองได้"));
                    }

                    current = byId[currentId].ParentId;
                }
            }

            var newSiblingSortOrders = categories
                .Where(c => c.Id != target.Id && c.ParentId == command.ParentId)
                .Select(c => c.SortOrder)
                .ToList();
            var newSortOrder = newSiblingSortOrders.Count > 0 ? newSiblingSortOrders.Max() + 1 : 0;

            target.MoveTo(command.ParentId);
            target.Reorder(newSortOrder);
        }

        target.Rename(command.NameTh, command.NameEn);
        target.ChangeSlug(command.Slug);
        target.SetIcon(command.IconKey);

        if (command.IsActive)
        {
            target.Activate();
        }
        else
        {
            target.Deactivate();
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            var slugNowTaken = await dbContext.Categories()
                .AsNoTracking()
                .AnyAsync(c => c.Id != id && c.Slug == command.Slug, cancellationToken)
                .ConfigureAwait(false);

            if (!slugNowTaken)
            {
                throw; // not the expected slug race — let the global exception handler log and 500 it
            }

            return Result.Failure<UpdateCategoryResponse>(DomainError.Conflict($"Slug '{command.Slug}' ถูกใช้แล้ว"));
        }

        await cache.InvalidateAsync(cancellationToken).ConfigureAwait(false);

        return new UpdateCategoryResponse(
            target.Id, target.Slug, target.NameTh, target.NameEn, target.IconKey,
            target.ParentId, target.SortOrder, target.IsActive);
    }
}
