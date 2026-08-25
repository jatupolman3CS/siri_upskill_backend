using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CreateCategory;

/// <summary>
/// Creates a new category. <see cref="Category.SortOrder"/> is computed here (append to end of the
/// target parent's current siblings) rather than accepted from the client. Slug uniqueness is
/// enforced by <c>Categories.Slug</c>'s unique index (case-insensitive for free — see
/// <c>CategoryConfiguration</c>'s doc comment); this handler checks first for a friendly 409 instead
/// of always relying on the DB round-trip to fail, but also catches the still-possible race (two
/// concurrent creates with the same slug) the same way <c>Register.RegisterHandler</c> does.
/// </summary>
public sealed class CreateCategoryHandler(AppDbContext dbContext, CategoryTreeCache cache)
{
    public async Task<Result<CreateCategoryResponse>> HandleAsync(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        if (command.ParentId is { } parentId)
        {
            var parentExists = await dbContext.Categories()
                .AsNoTracking()
                .AnyAsync(c => c.Id == parentId, cancellationToken)
                .ConfigureAwait(false);

            if (!parentExists)
            {
                return Result.Failure<CreateCategoryResponse>(DomainError.NotFound("ไม่พบหมวดหมู่แม่ที่ระบุ"));
            }
        }

        var slugTaken = await dbContext.Categories()
            .AsNoTracking()
            .AnyAsync(c => c.Slug == command.Slug, cancellationToken)
            .ConfigureAwait(false);

        if (slugTaken)
        {
            return Result.Failure<CreateCategoryResponse>(DomainError.Conflict($"Slug '{command.Slug}' ถูกใช้แล้ว"));
        }

        var siblingSortOrders = await dbContext.Categories()
            .AsNoTracking()
            .Where(c => c.ParentId == command.ParentId)
            .Select(c => c.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var nextSortOrder = siblingSortOrders.Count > 0 ? siblingSortOrders.Max() + 1 : 0;

        var category = Category.Create(command.Slug, command.NameTh, command.NameEn, command.IconKey, command.ParentId, nextSortOrder);
        dbContext.Categories().Add(category);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            var slugNowTaken = await dbContext.Categories()
                .AsNoTracking()
                .AnyAsync(c => c.Slug == command.Slug, cancellationToken)
                .ConfigureAwait(false);

            if (!slugNowTaken)
            {
                throw; // not the expected slug race — let the global exception handler log and 500 it
            }

            return Result.Failure<CreateCategoryResponse>(DomainError.Conflict($"Slug '{command.Slug}' ถูกใช้แล้ว"));
        }

        await cache.InvalidateAsync(cancellationToken).ConfigureAwait(false);

        return new CreateCategoryResponse(
            category.Id, category.Slug, category.NameTh, category.NameEn, category.IconKey,
            category.ParentId, category.SortOrder, category.IsActive);
    }
}
