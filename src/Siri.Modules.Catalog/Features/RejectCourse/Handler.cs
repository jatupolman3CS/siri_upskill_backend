using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.RejectCourse;

/// <summary>
/// Rejects a course under review — records why (<see cref="COURSE.RejectionReason"/>) so the instructor
/// knows what to fix (task P1-05's "validation rule + audit"). Checks <see cref="CourseStatus.InReview"/>
/// before calling <see cref="COURSE.Reject"/> — same "handler enforces the workflow stage, domain method
/// is the backstop" split <c>ApproveCourseHandler</c>/<c>COURSE.Reject</c>'s own doc comment describe. No
/// Sections/Episodes include needed here, unlike Approve/SubmitForReview — <c>Reject</c> never touches
/// the child graph.
/// </summary>
public sealed class RejectCourseHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotInReviewError = DomainError.Conflict("ปฏิเสธได้เฉพาะคอร์สที่อยู่ระหว่างตรวจสอบเท่านั้น");

    public async Task<Result<RejectCourseResponse>> HandleAsync(Guid courseId, RejectCourseCommand command, CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<RejectCourseResponse>(NotFoundError);
        }

        if (course.Status != CourseStatus.InReview)
        {
            return Result.Failure<RejectCourseResponse>(NotInReviewError);
        }

        course.Reject(command.Reason);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RejectCourseResponse(course.Id, course.Status, command.Reason);
    }
}
