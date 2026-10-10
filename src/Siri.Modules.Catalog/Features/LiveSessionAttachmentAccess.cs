using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Features;

/// <summary>
/// Who may see (and download) the materials of one live session, shared by the list and download handlers
/// (task P4-03c). Mirrors <see cref="EpisodeAccessHelper"/> for episodes: an admin, the course's own instructor,
/// or a learner with an <b>active, unexpired enrollment</b> in the session's course. There is no "free preview"
/// notion for sessions, so an anonymous caller never gets in.
/// </summary>
public static class LiveSessionAttachmentAccess
{
    /// <summary>
    /// The session, or <c>null</c> when it does not exist <b>or its course is soft-deleted</b> (the join keeps the
    /// <see cref="COURSE"/> soft-delete query filter in force — materials of a deleted course are gone, not just hidden).
    /// </summary>
    public static Task<COURSE_LIVE_SESSION?> FindSessionAsync(AppDbContext dbContext, Guid sessionId, CancellationToken cancellationToken) =>
        (from session in dbContext.CourseLiveSessions().AsNoTracking()
         join course in dbContext.Courses().AsNoTracking() on session.CourseId equals course.Id
         where session.Id == sessionId
         select session)
        .FirstOrDefaultAsync(cancellationToken);

    public static async Task<bool> CanReadAsync(
        ICatalogPriceContract ownershipVerifier,
        ILearningEnrollmentChecker enrollmentChecker,
        Guid courseId,
        Guid? userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownershipVerifier);
        ArgumentNullException.ThrowIfNull(enrollmentChecker);

        if (isAdmin)
        {
            return true;
        }

        if (userId is not { } uid || uid == Guid.Empty)
        {
            return false;
        }

        if (await ownershipVerifier.IsInstructorOwnerOfCourseAsync(courseId, uid, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await enrollmentChecker.HasActiveEnrollmentAsync(uid, courseId, cancellationToken).ConfigureAwait(false);
    }
}
