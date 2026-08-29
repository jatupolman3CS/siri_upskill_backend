using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Application;

public sealed class CourseStatsUpdater(AppDbContext dbContext) : ICourseStatsUpdater
{
    public async Task ReconcileCourseStatsAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        var course = await dbContext.Courses()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null) return;

        var reviews = await dbContext.CourseReviews()
            .Where(r => r.CourseId == courseId && r.IsPublished)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var avgRating = reviews.Count > 0
            ? Math.Round((decimal)reviews.Average(r => r.Rating), 1)
            : 0m;

        course.UpdateRatingStats(avgRating, reviews.Count);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReconcileAllCoursesStatsAsync(CancellationToken cancellationToken = default)
    {
        var courseIds = await dbContext.Courses()
            .Where(c => !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var id in courseIds)
        {
            await ReconcileCourseStatsAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }
}
