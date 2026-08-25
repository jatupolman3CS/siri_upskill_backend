using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetPendingCourseReviews;

/// <summary>Courses awaiting admin review, offset-paginated.</summary>
public sealed class GetPendingCourseReviewsHandler(AppDbContext dbContext)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<PagedResult<PendingCourseReviewSummary>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };

        var query = dbContext.Courses()
            .AsNoTracking()
            .Where(c => c.Status == CourseStatus.InReview)
            .OrderBy(c => c.UpdatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var courses = await query
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (courses.Count == 0)
        {
            return PagedResult<PendingCourseReviewSummary>.Create([], totalCount, effectivePage, effectivePageSize);
        }

        var instructorIds = courses.Select(c => c.InstructorId).Distinct().ToList();
        var categoryIds = courses.Select(c => c.CategoryId).Distinct().ToList();

        var instructors = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .Where(i => instructorIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        var categories = await dbContext.Categories()
            .AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.NameTh, cancellationToken)
            .ConfigureAwait(false);

        var items = courses.Select(c =>
        {
            instructors.TryGetValue(c.InstructorId, out var instructorName);
            categories.TryGetValue(c.CategoryId, out var categoryName);

            var durationMinutes = (int)Math.Ceiling(c.TotalDurationSeconds / 60.0);

            return new PendingCourseReviewSummary(
                c.Id,
                c.Slug,
                c.Title,
                c.InstructorId,
                instructorName,
                c.CategoryId,
                categoryName,
                c.Price,
                c.ThumbnailUrl,
                c.EpisodeCount,
                durationMinutes,
                c.UpdatedAtUtc);
        }).ToList();

        return PagedResult<PendingCourseReviewSummary>.Create(items, totalCount, effectivePage, effectivePageSize);
    }
}
