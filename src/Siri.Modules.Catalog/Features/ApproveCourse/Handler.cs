using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Catalog.Features.ApproveCourse;

/// <summary>
/// Approves a course under review — publishes it. Checks <see cref="CourseStatus.InReview"/> before
/// calling the existing <see cref="COURSE.Publish"/> (built in P1-02) rather than adding a separate
/// "approve" domain method: <c>Publish</c> already does exactly what an approval needs (sets Published +
/// <c>PublishedAtUtc</c> + re-validates the "has media" invariant as a backstop), and this handler is
/// what actually enforces "only from InReview" — see <c>COURSE.Publish</c>'s own doc comment for why that
/// split is deliberate.
/// <para>
/// <b>Output-cache invalidation (task P1-07)</b>: this handler evicts <see cref="CourseOutputCache.Tag"/>
/// because it moves a course from not-publicly-visible to visible. <c>UnpublishCourseHandler</c> now does
/// the same for the reverse transition (visible → not-visible) — this was the "whichever future handler
/// moves visibility must also call this same eviction" case this comment used to flag as unaddressed.
/// <c>UpdateCourseHandler</c>/<c>DeleteCourseHandler</c> still only ever act on <see cref="CourseStatus.Draft"/>
/// courses (their own Draft-only gate), which were never in the public cache to begin with, so neither needs
/// to invalidate anything — including "changed the price", since there is currently no way to change a
/// <em>Published</em> course's price at all until a later task adds that capability (whichever one does
/// must also call this same eviction).
/// </para>
/// </summary>
public sealed class ApproveCourseHandler(
    AppDbContext dbContext,
    IClock clock,
    IOutputCacheStore outputCacheStore,
    IMediaAssetContract mediaAssets,
    ILiveMeetingReadinessReader liveMeetingReadiness,
    CourseSearchIndexer searchIndexer)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotInReviewError = DomainError.Conflict("อนุมัติได้เฉพาะคอร์สที่อยู่ระหว่างตรวจสอบเท่านั้น");

    public async Task<Result<ApproveCourseResponse>> HandleAsync(Guid courseId, CancellationToken cancellationToken)
    {
        // Include Sections/Episodes: COURSE.Publish's own internal "has media" backstop walks that
        // in-memory graph — same reasoning SubmitCourseForReviewHandler's own comment gives.
        // LiveSessions (P11-01) is a sibling collection of Sections, not a child of it — a separate
        // .Include, not a .ThenInclude off Sections — needed for the same reason (CanPublishOrSubmit's
        // HasFutureScheduledLiveSession would otherwise always see an empty collection).
        var course = await dbContext.Courses()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .Include(c => c.LiveSessions)
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<ApproveCourseResponse>(NotFoundError);
        }

        if (course.Status != CourseStatus.InReview)
        {
            return Result.Failure<ApproveCourseResponse>(NotInReviewError);
        }

        var ownerUserId = await dbContext.InstructorProfiles().AsNoTracking()
            .Where(profile => profile.Id == course.InstructorId)
            .Select(profile => (Guid?)profile.UserId)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (ownerUserId is null)
        {
            return Result.Failure<ApproveCourseResponse>(DomainError.Validation("The course instructor could not be found."));
        }
        var mediaReadiness = await CourseMediaReadiness.ValidateAsync(course, ownerUserId.Value, mediaAssets, clock, cancellationToken).ConfigureAwait(false);
        if (mediaReadiness.IsFailure)
        {
            return Result.Failure<ApproveCourseResponse>(mediaReadiness.Error);
        }

        // P11-03: every future scheduled class of a Live/Hybrid course must still have a usable online room at
        // approval time (a Google token may have expired, or a class may have been added, since submission).
        var meetingReadiness = await LiveMeetingReadinessGate.ValidateAsync(course, liveMeetingReadiness, clock, cancellationToken).ConfigureAwait(false);
        if (meetingReadiness.IsFailure)
        {
            return Result.Failure<ApproveCourseResponse>(meetingReadiness.Error);
        }

        course.Publish(clock);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Only after the commit succeeds — same post-commit ordering every cache/mirror invalidation in
        // this codebase already follows (e.g. Login's SE-03 Redis eviction, RevokeSession's session-registry
        // removal).
        await outputCacheStore.EvictByTagAsync(CourseOutputCache.Tag, cancellationToken).ConfigureAwait(false);

        // Same moment, same reason: the course just became publicly visible, so the search index (Meilisearch) must learn about it now rather
        // than at the next hourly reindex. Best effort by design — it logs instead of throwing, so a search-engine outage never fails an approval.
        await searchIndexer.SyncCourseAsync(course.Id, cancellationToken).ConfigureAwait(false);

        return new ApproveCourseResponse(course.Id, course.Status, course.PublishedAtUtc);
    }
}
