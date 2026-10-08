using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.SetCourseLiveSettings;

/// <summary>
/// Turns the course's opt-in to Google Calendar attendee sync on or off (P11-04 §6/§6.1). When on, the invite job adds each invited
/// learner's e-mail address to the instructor's Google event so only invited people are admitted straight into the room — which
/// means learners' addresses go to Google, hence an explicit per-course choice of the owner (default off).
/// <para>
/// Ownership follows the Catalog's existing pattern (<c>SetCourseEnrollmentPolicyHandler</c>): <b>404</b> when the course does
/// not exist, <b>403</b> when it exists but belongs to another instructor — the two stay distinguishable because a course id is not
/// a secret. <b>409</b> for an archived course. No output-cache eviction: the flag is not part of the public read model.
/// </para>
/// </summary>
public sealed class SetCourseLiveSettingsHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");

    public async Task<Result<SetCourseLiveSettingsResponse>> HandleAsync(
        Guid userId,
        Guid courseId,
        SetCourseLiveSettingsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.GoogleAttendeeSyncEnabled is not { } enabled)
        {
            return Result.Failure<SetCourseLiveSettingsResponse>(
                DomainError.Validation("googleAttendeeSyncEnabled is required (true or false)."));
        }

        var course = await dbContext.Courses()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<SetCourseLiveSettingsResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<SetCourseLiveSettingsResponse>(NotOwnerError);
        }

        if (course.Status == CourseStatus.Archived)
        {
            return Result.Failure<SetCourseLiveSettingsResponse>(
                DomainError.Conflict($"Cannot change live settings of a course in {course.Status} status."));
        }

        course.SetGoogleAttendeeSync(enabled);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new SetCourseLiveSettingsResponse(course.Id, course.GoogleAttendeeSyncEnabled));
    }
}
