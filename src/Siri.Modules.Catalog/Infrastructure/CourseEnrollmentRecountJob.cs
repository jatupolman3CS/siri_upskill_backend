using Microsoft.Extensions.Logging;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Contracts;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// The recurring job <c>course-enrollment-recount</c> (hourly, registered only in <c>Siri.Workers</c>): compares every course's <c>ENROLLMENT_COUNT</c> with the
/// enrollments themselves and repairs the ones that differ (<see cref="ICourseEnrollmentCountUpdater.RecountDriftedAsync"/>). Two reasons it exists: rows that
/// existed before the counter had a writer become correct without a data migration, and any drift — a crashed request, an admin SQL fix, a status changed outside the
/// normal path — heals within the hour instead of living forever. Pages of <see cref="PageSize"/> courses; each correction is its own short, row-locked transaction, so
/// it runs alongside live enrollments and a second concurrent run is merely redundant. Never logs anything but counts.
/// </summary>
public sealed class CourseEnrollmentRecountJob(ICourseEnrollmentCountUpdater updater, ILogger<CourseEnrollmentRecountJob> logger)
{
    /// <summary>Courses compared per query.</summary>
    public const int PageSize = CourseEnrollmentCountUpdater.DefaultPageSize;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var corrected = await updater.RecountDriftedAsync(PageSize, cancellationToken).ConfigureAwait(false);

        if (corrected > 0)
        {
            logger.LogInformation("Course enrollment recount corrected {Count} course(s) whose stored enrollment count had drifted.", corrected);
        }
    }
}
