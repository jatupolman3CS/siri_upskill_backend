using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure.Contracts;

/// <summary>
/// Learning's side of Catalog's <c>ENROLLMENT_COUNT</c> recount: the number of enrollments per course that count (status Active or Expired — see
/// <c>Siri.Modules.Learning.Application.EnrollmentCountRules</c>). One grouped query over <c>IX(COURSE_ID, STATUS)</c> for the whole set; the same rule as the instructor
/// dashboard's distinct-learner KPI (<see cref="LearningAnalyticsContract"/>), which — because <c>UNIQUE (USER_ID, COURSE_ID)</c> — is the same number per course.
/// Uses the scoped context it is given, so inside a caller's transaction it sees that transaction (and, after a lock, everything committed so far).
/// </summary>
public sealed class LearningEnrollmentCounter(AppDbContext dbContext) : ILearningEnrollmentCounter
{
    public async Task<IReadOnlyDictionary<Guid, int>> CountEnrollmentsAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(courseIds);

        var ids = courseIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var counts = ids.ToDictionary(id => id, _ => 0);
        if (ids.Length == 0)
        {
            return counts;
        }

        // Keep this predicate in step with EnrollmentCountRules.Counts (everything but Revoked); written out because EF must translate it.
        var grouped = await dbContext.Enrollments()
            .AsNoTracking()
            .Where(e => ids.Contains(e.COURSE_ID) && (e.STATUS == EnrollmentStatus.Active || e.STATUS == EnrollmentStatus.Expired))
            .GroupBy(e => e.COURSE_ID)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in grouped)
        {
            counts[row.CourseId] = row.Count;
        }

        return counts;
    }
}
