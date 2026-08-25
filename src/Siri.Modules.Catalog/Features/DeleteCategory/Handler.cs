using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DeleteCategory;

/// <summary>
/// Hard-deletes a category (see <c>Domain.Category</c>'s doc comment for why this is correct — not in
/// DATABASE.md's soft-delete table list, admin-curated taxonomy, not user content). Rejects if the
/// category still has children — <see cref="Infrastructure.CategoryConfiguration"/>'s self-referencing
/// FK (<c>OnDelete(Restrict)</c>) is the DB-level backstop for the race where a child is inserted
/// between this check and <c>SaveChangesAsync</c>.
/// </summary>
public sealed class DeleteCategoryHandler(AppDbContext dbContext, CategoryTreeCache cache)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (category is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบหมวดหมู่นี้"));
        }

        var hasChildren = await dbContext.Categories()
            .AsNoTracking()
            .AnyAsync(c => c.ParentId == id, cancellationToken)
            .ConfigureAwait(false);

        if (hasChildren)
        {
            return Result.Failure(DomainError.Conflict("ไม่สามารถลบหมวดหมู่ที่มีหมวดหมู่ย่อยอยู่ได้ ย้ายหรือลบหมวดหมู่ย่อยก่อน"));
        }

        dbContext.Categories().Remove(category);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Backstop for the race documented on the class: a child was inserted under this category
            // between the AnyAsync check above and this save, and the FK's OnDelete(Restrict) refused
            // the delete at the database level.
            return Result.Failure(DomainError.Conflict("ไม่สามารถลบหมวดหมู่ที่มีหมวดหมู่ย่อยอยู่ได้ ย้ายหรือลบหมวดหมู่ย่อยก่อน"));
        }

        await cache.InvalidateAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
