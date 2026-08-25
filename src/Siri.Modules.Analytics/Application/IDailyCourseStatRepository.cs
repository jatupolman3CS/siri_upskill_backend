using Siri.Modules.Analytics.Domain;

namespace Siri.Modules.Analytics.Application;

public sealed record CourseStatAggregate(
    Guid CourseId,
    decimal TotalRevenue,
    int TotalEnrollments,
    int TotalViews);

/// <summary>
/// Read-mostly repository over <see cref="DAILY_COURSE_STAT"/> rows.
/// </summary>
public interface IDailyCourseStatRepository
{
    Task<DAILY_COURSE_STAT?> GetAsync(DateOnly date, Guid courseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DAILY_COURSE_STAT>> GetForCourseAsync(Guid courseId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);

    Task<IReadOnlyList<CourseStatAggregate>> GetTopCoursesAsync(DateOnly fromDate, DateOnly toDate, int limit, CancellationToken cancellationToken);
}
