using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.SetCourseEnrollmentPolicy;

public sealed class SetCourseEnrollmentPolicyHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");

    public async Task<Result<SetCourseEnrollmentPolicyResponse>> HandleAsync(
        Guid userId,
        Guid courseId,
        SetCourseEnrollmentPolicyCommand command,
        CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<SetCourseEnrollmentPolicyResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<SetCourseEnrollmentPolicyResponse>(NotOwnerError);
        }

        if (course.Status == CourseStatus.Archived)
        {
            return Result.Failure<SetCourseEnrollmentPolicyResponse>(
                DomainError.Conflict($"Cannot change enrollment policy of a course in {course.Status} status."));
        }

        if (command.EnrollmentDeadlineUtc is { Kind: not DateTimeKind.Utc })
        {
            return Result.Failure<SetCourseEnrollmentPolicyResponse>(
                DomainError.Validation("enrollmentDeadlineUtc must be UTC (ISO-8601 with Z suffix)."));
        }

        try
        {
            course.SetEnrollmentPolicy(command.EnrollmentDeadlineUtc, command.MaxSeats);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result.Failure<SetCourseEnrollmentPolicyResponse>(DomainError.Validation(ex.Message));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // No output cache eviction here — unlike SetCourseDeliveryFormatHandler, none of these fields
        // appear in the public read model (GET /courses/{slug}, courses/search) — see contract §3 step 7
        // / §1 out-of-scope.
        return Result.Success(new SetCourseEnrollmentPolicyResponse(course.Id, course.EnrollmentDeadlineUtc, course.MaxSeats, course.SeatsUsed));
    }
}
