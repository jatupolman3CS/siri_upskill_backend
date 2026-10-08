using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Catalog.Features.SubmitCourseForReview;

/// <summary>
/// Submits one of the caller's own draft (or previously-rejected) courses for admin review. Same
/// ownership check shape as <c>UpdateCourseHandler</c>/<c>DeleteCourseHandler</c> (distinct 404/403, no
/// admin-bypass — see that class's own doc comment). Re-checks the same from-status and "has media"
/// invariant <see cref="COURSE.SubmitForReview"/> itself guards, purely to turn what would
/// otherwise be an uncaught <see cref="InvalidOperationException"/> into a friendly
/// <see cref="Result{TValue}"/> — the exact same "handler checks first for a friendly response, domain
/// method re-checks as its own backstop" split <c>ApproveInstructorApplicationHandler</c> already
/// establishes for <c>INSTRUCTOR_PROFILE.Approve</c>.
/// <para>
/// P11-01 (docs/contracts/P11-01-catalog-live-sessions.md §2.4): the "has media" pre-check below is now
/// <see cref="COURSE.DeliveryFormat"/>-aware, mirroring <c>COURSE</c>'s own internal
/// <c>CanPublishOrSubmit</c> gate exactly (a Live/Hybrid course can satisfy it with a future scheduled
/// live session alone, no episode media required) — same reasoning <see cref="CourseMediaReadiness"/>'s
/// own doc comment gives for why it needed the same extension.
/// </para>
/// </summary>
public sealed class SubmitCourseForReviewHandler(
    AppDbContext dbContext,
    IMediaAssetContract mediaAssets,
    IClock clock,
    ILiveMeetingReadinessReader liveMeetingReadiness)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์ส่งคอร์สนี้เข้าตรวจสอบ");
    private static readonly DomainError WrongStatusError =
        DomainError.Conflict("ส่งตรวจสอบได้เฉพาะคอร์สที่เป็นฉบับร่างหรือถูกปฏิเสธเท่านั้น");
    private static readonly DomainError NoMediaError =
        DomainError.Validation("คอร์สต้องมีอย่างน้อย 1 บทเรียนที่แนบวิดีโอแล้วก่อนส่งตรวจสอบ");
    private static readonly DomainError NoLiveScheduleOrMediaError =
        DomainError.Validation("คอร์ส Live/Hybrid ต้องมีคาบสอนสดในอนาคตอย่างน้อย 1 คาบ หรือมีบทเรียนที่แนบวิดีโอแล้ว");

    public async Task<Result<SubmitCourseForReviewResponse>> HandleAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
    {
        // Include Sections/Episodes: both this handler's own "has media" pre-check and
        // COURSE.SubmitForReview's internal backstop walk that same in-memory graph — without loading
        // it, both would see an empty collection (no lazy-loading configured in this codebase) and
        // incorrectly reject a course that genuinely does have media attached. LiveSessions is a
        // sibling collection of Sections on COURSE (P11-01), not a child of it — a separate .Include,
        // not a .ThenInclude off Sections — needed for the same reason (CanPublishOrSubmit's
        // HasFutureScheduledLiveSession would otherwise always see an empty collection).
        var course = await dbContext.Courses()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .Include(c => c.LiveSessions)
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

        // Mirrors COURSE's own internal CanPublishOrSubmit gate exactly (see that method's doc comment):
        // OnDemand needs at least one episode with media; Live/Hybrid can satisfy this with a future
        // scheduled live session instead.
        var hasEpisodeWithMedia = course.Sections.SelectMany(s => s.Episodes).Any(e => e.MediaAssetId is not null);
        var hasFutureScheduledLiveSession = course.LiveSessions.Any(s => s.Status == CourseLiveSessionStatus.Scheduled && s.StartsAtUtc > clock.UtcNow);
        var canSubmit = course.DeliveryFormat == DeliveryFormat.OnDemand
            ? hasEpisodeWithMedia
            : hasFutureScheduledLiveSession || hasEpisodeWithMedia;

        if (!canSubmit)
        {
            return Result.Failure<SubmitCourseForReviewResponse>(
                course.DeliveryFormat == DeliveryFormat.OnDemand ? NoMediaError : NoLiveScheduleOrMediaError);
        }

        var mediaReadiness = await CourseMediaReadiness.ValidateAsync(course, userId, mediaAssets, clock, cancellationToken).ConfigureAwait(false);
        if (mediaReadiness.IsFailure)
        {
            return Result.Failure<SubmitCourseForReviewResponse>(mediaReadiness.Error);
        }

        // P11-03: every future scheduled class of a Live/Hybrid course must already have a usable online room.
        var meetingReadiness = await LiveMeetingReadinessGate.ValidateAsync(course, liveMeetingReadiness, clock, cancellationToken).ConfigureAwait(false);
        if (meetingReadiness.IsFailure)
        {
            return Result.Failure<SubmitCourseForReviewResponse>(meetingReadiness.Error);
        }

        course.SubmitForReview(clock);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SubmitCourseForReviewResponse(course.Id, course.Status);
    }
}
