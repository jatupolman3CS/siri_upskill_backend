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
    string UserName,
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

        var allRatings = await query.Select(r => r.Rating).ToListAsync(cancellationToken).ConfigureAwait(false);
        var avg = allRatings.Count > 0 ? Math.Round((decimal)allRatings.Average(), 1) : 0m;
        var dist = new RatingDistributionResponse(
            allRatings.Count(r => r == 5),
            allRatings.Count(r => r == 4),
            allRatings.Count(r => r == 3),
            allRatings.Count(r => r == 2),
            allRatings.Count(r => r == 1));

        var items = reviews.Select(r =>
        {
            contacts.TryGetValue(r.UserId, out var contact);
            return new CourseReviewItemResponse(
                r.Id,
                r.CourseId,
                r.UserId,
                contact.DisplayName ?? "ผู้เรียน",
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
