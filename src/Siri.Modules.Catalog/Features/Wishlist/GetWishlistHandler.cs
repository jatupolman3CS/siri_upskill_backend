using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.Wishlist;

public sealed class GetWishlistHandler(AppDbContext dbContext)
{
    public async Task<Result<IReadOnlyList<WishlistCourseItemResponse>>> HandleAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var items = await (
            from w in dbContext.Wishlists().AsNoTracking()
            where w.UserId == userId
            join c in dbContext.Courses().AsNoTracking() on w.CourseId equals c.Id
            join p in dbContext.InstructorProfiles().AsNoTracking() on c.InstructorId equals p.UserId into profs
            from p in profs.DefaultIfEmpty()
            where c.Status == CourseStatus.Published && !c.IsDeleted
            orderby w.CreatedAtUtc descending
            select new WishlistCourseItemResponse(
                c.Id,
                c.Slug,
                c.Title,
                c.Subtitle,
                c.ThumbnailUrl,
                c.Price,
                c.ComparePrice,
                c.Currency,
                p != null ? p.DisplayName : "ผู้สอน",
                c.RatingAverage,
                c.RatingCount,
                w.CreatedAtUtc)
        ).ToListAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success<IReadOnlyList<WishlistCourseItemResponse>>(items);
    }
}
