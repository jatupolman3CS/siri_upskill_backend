using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure.Contracts;

/// <summary>
/// Implementation of <see cref="IInstructorCourseStatsReader"/> (docs/contracts/P11-10-instructor-dashboard-summary.md §2.1). Two small reads: the
/// instructor profile of the user (so a profile with no courses still reports its id) and then that profile's courses — <c>Courses()</c> carries the
/// global soft-delete filter, so a deleted course never appears.
/// </summary>
public sealed class InstructorCourseStatsReader(AppDbContext dbContext) : IInstructorCourseStatsReader
{
    /// <summary>Largest number of courses returned (the dashboard shows at most 50 and never needs more).</summary>
    public const int MaxCourses = 200;

    public async Task<InstructorCourseStatsInfo> GetByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken)
    {
        if (instructorUserId == Guid.Empty)
        {
            return new InstructorCourseStatsInfo(null, []);
        }

        var profileId = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .Where(p => p.UserId == instructorUserId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (profileId is not { } id)
        {
            return new InstructorCourseStatsInfo(null, []);
        }

        var courses = await ReadCoursesAsync(id, cancellationToken).ConfigureAwait(false);

        return new InstructorCourseStatsInfo(id, courses);
    }

    /// <summary>The profile's non-deleted courses, newest first, at most <see cref="MaxCourses"/>. Internal so a unit test can prove the query translates to SQL.</summary>
    internal async Task<IReadOnlyList<InstructorCourseStat>> ReadCoursesAsync(Guid instructorProfileId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Courses()
            .AsNoTracking()
            .Where(c => c.InstructorId == instructorProfileId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id)
            .Take(MaxCourses)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.Slug,
                c.Status,
                c.DeliveryFormat,
                c.Price,
                c.EnrollmentCount,
                c.RatingAverage,
                c.RatingCount,
                c.ThumbnailUrl,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Enum names are mapped in memory (not in the SQL projection) so no provider-specific enum-to-string translation is relied on.
        return rows
            .Select(r => new InstructorCourseStat(
                r.Id,
                r.Title,
                r.Slug,
                r.Status.ToString(),
                r.DeliveryFormat.ToString(),
                r.Price,
                r.EnrollmentCount,
                r.RatingAverage,
                r.RatingCount,
                r.ThumbnailUrl))
            .ToList();
    }
}
