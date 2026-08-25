using Microsoft.EntityFrameworkCore;
using Siri.Modules.Analytics.Application;
using Siri.Modules.Analytics.Domain;
using Siri.Persistence;

namespace Siri.Modules.Analytics.Infrastructure;

public sealed class DailyCourseStatRepository(AppDbContext dbContext) : IDailyCourseStatRepository
{
    public async Task<DAILY_COURSE_STAT?> GetAsync(DateOnly date, Guid courseId, CancellationToken cancellationToken) =>
        await dbContext.DailyCourseStats()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.DATE == date && x.COURSE_ID == courseId, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<DAILY_COURSE_STAT>> GetForCourseAsync(Guid courseId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        await dbContext.DailyCourseStats()
            .AsNoTracking()
            .Where(x => x.COURSE_ID == courseId && x.DATE >= fromDate && x.DATE <= toDate)
            .OrderBy(x => x.DATE)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<CourseStatAggregate>> GetTopCoursesAsync(DateOnly fromDate, DateOnly toDate, int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 50);

        var top = await dbContext.DailyCourseStats()
            .AsNoTracking()
            .Where(x => x.DATE >= fromDate && x.DATE <= toDate)
            .GroupBy(x => x.COURSE_ID)
            .Select(g => new CourseStatAggregate(
                g.Key,
                g.Sum(x => x.REVENUE),
                g.Sum(x => x.ENROLLMENTS),
                g.Sum(x => x.VIEWS)))
            .OrderByDescending(x => x.TotalRevenue)
            .ThenByDescending(x => x.TotalEnrollments)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return top;
    }
}
