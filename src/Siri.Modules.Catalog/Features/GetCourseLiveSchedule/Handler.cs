using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateLiveSession;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetCourseLiveSchedule;

/// <summary>
/// Reads a course's live schedule for its owning instructor. Ownership is the Catalog convention: an unknown course is 404, a course owned by someone else is 403,
/// and an administrator is <em>not</em> an owner (moderation is a separate admin feature). Read-only and untracked: narrow projections (the course's scalar
/// settings, the caller's instructor profile id, then the sessions) rather than loading the whole aggregate. The session mapping is the same one the write endpoints
/// return (<see cref="CreateLiveSessionHandler.ToResponse"/>), so the panel sees one shape everywhere.
/// </summary>
public sealed class GetCourseLiveScheduleHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์เข้าถึงคอร์สนี้");

    /// <summary>The scalar facts of a course this endpoint reports.</summary>
    internal sealed record CourseFacts(
        Guid Id,
        Guid InstructorId,
        DeliveryFormat DeliveryFormat,
        CourseStatus Status,
        DateTime? EnrollmentDeadlineUtc,
        int? MaxSeats,
        int SeatsUsed,
        bool GoogleAttendeeSyncEnabled);

    public async Task<Result<CourseLiveScheduleResponse>> HandleAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
    {
        var course = await FindCourseAsync(courseId, cancellationToken).ConfigureAwait(false);
        if (course is null)
        {
            return Result.Failure<CourseLiveScheduleResponse>(NotFoundError);
        }

        var instructorProfileId = await FindInstructorProfileIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (instructorProfileId is null || course.InstructorId != instructorProfileId)
        {
            return Result.Failure<CourseLiveScheduleResponse>(NotOwnerError);
        }

        var sessions = await ListSessionsAsync(courseId, cancellationToken).ConfigureAwait(false);

        return new CourseLiveScheduleResponse(
            course.Id,
            course.DeliveryFormat,
            course.Status,
            course.EnrollmentDeadlineUtc,
            course.MaxSeats,
            course.SeatsUsed,
            course.GoogleAttendeeSyncEnabled,
            sessions.Select(CreateLiveSessionHandler.ToResponse).ToList());
    }

    // The three reads, separate and internal so a unit test can prove each one translates to SQL without a database.

    /// <summary>The course (the global soft-delete filter hides a deleted one, so it reads as absent).</summary>
    internal Task<CourseFacts?> FindCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        dbContext.Courses()
            .AsNoTracking()
            .Where(c => c.Id == courseId)
            .Select(c => new CourseFacts(
                c.Id,
                c.InstructorId,
                c.DeliveryFormat,
                c.Status,
                c.EnrollmentDeadlineUtc,
                c.MaxSeats,
                c.SeatsUsed,
                c.GoogleAttendeeSyncEnabled))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The caller's instructor profile id, or <c>null</c> when they have none.</summary>
    internal Task<Guid?> FindInstructorProfileIdAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.InstructorProfiles()
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Every session of the course in every status, by start time.</summary>
    internal Task<List<COURSE_LIVE_SESSION>> ListSessionsAsync(Guid courseId, CancellationToken cancellationToken) =>
        dbContext.CourseLiveSessions()
            .AsNoTracking()
            .Where(s => s.CourseId == courseId)
            .OrderBy(s => s.StartsAtUtc).ThenBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
}
