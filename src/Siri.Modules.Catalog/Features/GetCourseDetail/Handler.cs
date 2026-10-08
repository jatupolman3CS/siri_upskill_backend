using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetCourseDetail;

/// <summary>
/// Public course detail by slug — task P1-07's "course... detail projection". Only ever
/// <see cref="CourseStatus.Published"/> (same rule as <c>SearchCoursesHandler</c>): a Draft/InReview/
/// Rejected course's slug returns the exact same 404 as a slug that was never registered at all, so
/// nothing about an unpublished course's existence leaks through this endpoint either.
/// </summary>
public sealed class GetCourseDetailHandler(AppDbContext dbContext, IUserContext userContext, IClock clock)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");

    public async Task<Result<CourseDetailResponse>> HandleAsync(string slug, CancellationToken cancellationToken)
    {
        var decodedSlug = Uri.UnescapeDataString(slug).Trim();
        var generatedSlug = ThaiSlugGenerator.GenerateBaseSlug(decodedSlug);

        var course = await dbContext.Courses()
            .AsNoTracking()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .Include(c => c.Outcomes)
            .Include(c => c.Requirements)
            .FirstOrDefaultAsync(c => 
                (c.Slug == slug || c.Slug == decodedSlug || c.Slug == generatedSlug || c.Title == decodedSlug) 
                && c.Status == CourseStatus.Published, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<CourseDetailResponse>(NotFoundError);
        }

        var instructor = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .Where(p => p.Id == course.InstructorId)
            .Select(p => new CourseDetailInstructor(p.Id, p.DisplayName, p.Headline, p.AvatarUrl))
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var sections = course.Sections
            .OrderBy(s => s.SortOrder)
            .Select(s => new CourseDetailSection(
                s.Id,
                s.Title,
                s.SortOrder,
                s.Episodes
                    .OrderBy(e => e.SortOrder)
                    .Select(e => new CourseDetailEpisode(e.Id, e.Title, e.SortOrder, e.DurationSeconds, e.IsFreePreview))
                    .ToList()))
            .ToList();

        var outcomes = course.Outcomes.OrderBy(o => o.SortOrder).Select(o => o.Text).ToList();
        var requirements = course.Requirements.OrderBy(r => r.SortOrder).Select(r => r.Text).ToList();

        var isWishlisted = userContext.UserId.HasValue && await dbContext.Wishlists()
            .AsNoTracking()
            .AnyAsync(w => w.UserId == userContext.UserId.Value && w.CourseId == course.Id, cancellationToken)
            .ConfigureAwait(false);

        CourseDetailLiveSchedule? liveSchedule = null;
        if (course.DeliveryFormat != DeliveryFormat.OnDemand)
        {
            var sessions = await dbContext.CourseLiveSessions()
                .AsNoTracking()
                .Where(s => s.CourseId == course.Id && s.Status == CourseLiveSessionStatus.Scheduled)
                .OrderBy(s => s.StartsAtUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var nowUtc = clock.UtcNow;
            var detailSessions = sessions.Select(s => ToDetailSession(s, nowUtc)).ToList();

            var upcomingCount = detailSessions.Count(s => s.DisplayState is LiveSessionDisplayState.Upcoming or LiveSessionDisplayState.Live);
            var pastCount = detailSessions.Count(s => s.DisplayState == LiveSessionDisplayState.Ended);
            var nextStartsAtUtc = detailSessions.FirstOrDefault(s => s.DisplayState != LiveSessionDisplayState.Ended)?.StartsAtUtc;

            liveSchedule = new CourseDetailLiveSchedule(
                "Asia/Bangkok",
                upcomingCount,
                pastCount,
                nextStartsAtUtc,
                detailSessions);
        }

        return new CourseDetailResponse(
            course.Id, course.Slug, course.Title, course.Subtitle, course.Description,
            course.Level, course.Language, course.ThumbnailUrl, course.Price, course.ComparePrice, course.Currency,
            course.AccessDurationDays, course.RatingAverage, course.RatingCount, course.EnrollmentCount,
            course.EpisodeCount, course.TotalDurationSeconds, course.SeoTitle, course.SeoDescription,
            course.PublishedAtUtc, course.CategoryId, instructor, outcomes, requirements, sections, isWishlisted,
            course.DeliveryFormat, liveSchedule);
    }

    /// <summary>The public view of one scheduled class. No room link anywhere: there is no URL property, and the title — instructor free text,
    /// shown to anonymous visitors — is scrubbed of meeting-host links too, so a title stored before the write-side rule existed (or through any
    /// other path) cannot leak one.</summary>
    internal static CourseDetailLiveSession ToDetailSession(COURSE_LIVE_SESSION session, DateTime nowUtc) => new(
        session.Id,
        MeetingLinkText.ScrubRequired(session.Title),
        session.StartsAtUtc,
        session.EndsAtUtc,
        LiveSessionDisplayStateCalculator.Compute(LiveSessionStatus.Scheduled, session.StartsAtUtc, session.EndsAtUtc, nowUtc),
        session.RecordingEpisodeId.HasValue);
}
