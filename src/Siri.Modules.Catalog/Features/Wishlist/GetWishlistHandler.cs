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
            // COURSE.InstructorId is a FK to INSTRUCTOR_PROFILE.Id (see CourseConfiguration.cs), not
            // InstructorProfile.UserId — those are different columns. Joining on UserId here previously
            // resolved to null via DefaultIfEmpty() for every row, silently falling back to the generic
            // "ผู้สอน" placeholder for every wishlisted course. Fixed to match the FK's actual target,
            // consistent with EpisodeAccessHelper.cs's `course.InstructorId == instructorProfile.Id`.
            join p in dbContext.InstructorProfiles().AsNoTracking() on c.InstructorId equals p.Id into profs
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
                p != null ? p.DisplayName : null,
                c.RatingAverage,
                c.RatingCount,
                w.CreatedAtUtc)
        ).ToListAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success<IReadOnlyList<WishlistCourseItemResponse>>(items);
    }
}
