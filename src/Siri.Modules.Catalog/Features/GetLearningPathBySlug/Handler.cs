using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateLearningPath;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetLearningPathBySlug;

public sealed class GetLearningPathBySlugHandler(AppDbContext dbContext)
{
    public async Task<Result<LearningPathDetailResponse>> HandleAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var normalizedSlug = slug.Trim().ToLowerInvariant();

        var path = await dbContext.LearningPaths()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == normalizedSlug && p.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (path is null)
        {
            return Result.Failure<LearningPathDetailResponse>(DomainError.NotFound("ไม่พบเส้นทางการเรียนที่ระบุ"));
        }

        var courseDetails = await (
            from item in dbContext.LearningPathItems().AsNoTracking()
            join course in dbContext.Courses().AsNoTracking() on item.CourseId equals course.Id
            where item.PathId == path.Id
            orderby item.SortOrder
            select new LearningPathCourseItemResponse(course.Id, course.Title, course.Slug, item.SortOrder)
        ).ToListAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new LearningPathDetailResponse(
            path.Id,
            path.Slug,
            path.Title,
            path.Description,
            path.SortOrder,
            path.IsActive,
            courseDetails,
            path.CreatedAtUtc));
    }
}
