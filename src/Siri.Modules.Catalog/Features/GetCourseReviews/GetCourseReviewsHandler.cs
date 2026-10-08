using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetCourseReviews;

public sealed record RatingDistributionResponse(
    int Star5,
    int Star4,
    int Star3,
    int Star2,
    int Star1);

public sealed record CourseReviewSummaryResponse(
    decimal RatingAverage,
    int RatingCount,
    RatingDistributionResponse Distribution,
    IReadOnlyList<CourseReviewItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record CourseReviewItemResponse(
    Guid Id,
    Guid CourseId,
    Guid UserId,
    string? UserName,
    int Rating,
    string? Comment,
    DateTime CreatedAtUtc);

public sealed class GetCourseReviewsHandler(
    AppDbContext dbContext,
    IUserContactReader userContactReader)
{
    public async Task<Result<CourseReviewSummaryResponse>> HandleAsync(
        Guid courseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var effectivePage = page <= 0 ? 1 : page;
        var effectivePageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 50);

        var query = dbContext.CourseReviews()
            .AsNoTracking()
            .Where(r => r.CourseId == courseId && r.IsPublished);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var reviews = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var userIds = reviews.Select(r => r.UserId).Distinct().ToList();
        var contacts = await userContactReader.GetUsersContactInfoAsync(userIds, cancellationToken).ConfigureAwait(false);

        // RatingAverage is already denormalized on COURSE (CourseConfiguration.cs, kept in sync by
        // CreateCourseReviewHandler's COURSE.UpdateRatingStats call on every review create/update) — read
        // it instead of re-averaging the entire unbounded review set on every paginated page request.
        var avg = await dbContext.Courses()
            .AsNoTracking()
            .Where(c => c.Id == courseId)
            .Select(c => (decimal?)c.RatingAverage)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        // One GROUP BY aggregate (at most 5 rows back) instead of pulling every published review's rating
        // into memory just to run five separate .Count(r => r == N) passes over it.
        var ratingCounts = await query
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int CountForStar(int star) => ratingCounts.Find(x => x.Rating == star)?.Count ?? 0;

        var dist = new RatingDistributionResponse(
            CountForStar(5),
            CountForStar(4),
            CountForStar(3),
            CountForStar(2),
            CountForStar(1));

        var items = reviews.Select(r =>
        {
            contacts.TryGetValue(r.UserId, out var contact);
            return new CourseReviewItemResponse(
                r.Id,
                r.CourseId,
                r.UserId,
                string.IsNullOrWhiteSpace(contact.DisplayName) ? null : contact.DisplayName,
                r.Rating,
                r.Comment,
                r.CreatedAtUtc);
        }).ToList();

        return Result.Success(new CourseReviewSummaryResponse(
            avg,
            totalCount,
            dist,
            items,
            totalCount,
            effectivePage,
            effectivePageSize));
    }
}
