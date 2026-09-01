using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.Wishlist;

public sealed class AddToWishlistHandler(AppDbContext dbContext, IClock clock)
{
    public async Task<Result> HandleAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        var courseExists = await dbContext.Courses()
            .AsNoTracking()
            .AnyAsync(c => c.Id == courseId && c.Status == CourseStatus.Published && !c.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        if (!courseExists)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบคอร์สที่ระบุ"));
        }

        // Read-only check (only ever null-checked below, never mutated) — AsNoTracking per database.md.
        var existing = await dbContext.Wishlists()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId && w.CourseId == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var item = WISHLIST_ITEM.Create(userId, courseId, clock);
            dbContext.Wishlists().Add(item);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
