using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Catalog.Features.SubmitCourseForReview;

/// <summary>
/// Submits one of the caller's own draft (or previously-rejected) courses for admin review. Same
/// ownership check shape as <c>UpdateCourseHandler</c>/<c>DeleteCourseHandler</c> (distinct 404/403, no
/// admin-bypass — see that class's own doc comment). Re-checks the same from-status and "has episode with
/// media" conditions <see cref="COURSE.SubmitForReview"/> itself guards, purely to turn what would
/// otherwise be an uncaught <see cref="InvalidOperationException"/> into a friendly
/// <see cref="Result{TValue}"/> — the exact same "handler checks first for a friendly response, domain
/// method re-checks as its own backstop" split <c>ApproveInstructorApplicationHandler</c> already
/// establishes for <c>INSTRUCTOR_PROFILE.Approve</c>.
/// </summary>
public sealed class SubmitCourseForReviewHandler(AppDbContext dbContext, IMediaAssetContract mediaAssets)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์ส่งคอร์สนี้เข้าตรวจสอบ");
    private static readonly DomainError WrongStatusError =
        DomainError.Conflict("ส่งตรวจสอบได้เฉพาะคอร์สที่เป็นฉบับร่างหรือถูกปฏิเสธเท่านั้น");
    private static readonly DomainError NoMediaError =
        DomainError.Validation("คอร์สต้องมีอย่างน้อย 1 บทเรียนที่แนบวิดีโอแล้วก่อนส่งตรวจสอบ");

    public async Task<Result<SubmitCourseForReviewResponse>> HandleAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
    {
        // Include Sections/Episodes: both this handler's own "has media" pre-check and
        // COURSE.SubmitForReview's internal backstop walk that same in-memory graph — without loading
        // it, both would see an empty collection (no lazy-loading configured in this codebase) and
        // incorrectly reject a course that genuinely does have media attached.
        var course = await dbContext.Courses()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<SubmitCourseForReviewResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<SubmitCourseForReviewResponse>(NotOwnerError);
        }

        if (course.Status is not (CourseStatus.Draft or CourseStatus.Rejected))
        {
            return Result.Failure<SubmitCourseForReviewResponse>(WrongStatusError);
        }

        if (!course.Sections.SelectMany(s => s.Episodes).Any(e => e.MediaAssetId is not null))
        {
            return Result.Failure<SubmitCourseForReviewResponse>(NoMediaError);
        }

        var mediaReadiness = await CourseMediaReadiness.ValidateAsync(course, userId, mediaAssets, cancellationToken).ConfigureAwait(false);
        if (mediaReadiness.IsFailure)
        {
            return Result.Failure<SubmitCourseForReviewResponse>(mediaReadiness.Error);
        }

        course.SubmitForReview();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SubmitCourseForReviewResponse(course.Id, course.Status);
    }
}
