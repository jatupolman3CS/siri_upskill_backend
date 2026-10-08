using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>Read-side implementation of <see cref="ILiveScheduleReader"/> — queries
/// <see cref="COURSE_LIVE_SESSION"/> directly (via <see cref="AppDbContextCatalogExtensions.CourseLiveSessions"/>)
/// and maps into <see cref="Contracts"/> records so callers outside this module never see
/// <see cref="Domain.COURSE_LIVE_SESSION"/>/<see cref="Domain.CourseLiveSessionStatus"/> — same
/// module-boundary discipline <see cref="Contracts.CatalogPriceContract"/> already follows.</summary>
public sealed class LiveScheduleReader(AppDbContext dbContext) : ILiveScheduleReader
{
    public async Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        return await dbContext.CourseLiveSessions()
            .AsNoTracking()
            .Where(s => s.CourseId == courseId)
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => ToLiveSessionInfo(s))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        return await dbContext.CourseLiveSessions()
            .AsNoTracking()
            .Where(s => s.Status == CourseLiveSessionStatus.Scheduled && s.StartsAtUtc >= fromUtc && s.StartsAtUtc < toUtc)
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => ToLiveSessionInfo(s))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return await dbContext.CourseLiveSessions()
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => ToLiveSessionInfo(s))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // ---- P11-03: LiveSessionContext queries --------------------------------------------------------
    // Every query joins session -> course -> instructor profile in ONE statement (no per-row lookups). The global
    // soft-delete filter on COURSE drops sessions of deleted courses by itself. Sorting happens before the final
    // projection, and the projection passes plain columns (not entities) into a client-side mapper so only the needed
    // columns are fetched.

    public async Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().Take(LiveScheduleLimits.MaxWindowItems).ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var rows =
            from s in dbContext.CourseLiveSessions().AsNoTracking()
            join c in dbContext.Courses().AsNoTracking() on s.CourseId equals c.Id
            join p in dbContext.InstructorProfiles().AsNoTracking() on c.InstructorId equals p.Id
            select new { s, c, p };

        return await rows
            .Where(x => ids.Contains(x.s.Id))
            .OrderBy(x => x.s.StartsAtUtc).ThenBy(x => x.s.Id)
            .Select(x => ToContext(
                x.s.Id, x.s.CourseId, x.c.Title, x.c.Slug, x.s.Title, x.s.Description, x.s.StartsAtUtc, x.s.EndsAtUtc, x.s.Status,
                x.s.CancelReason, x.s.RecordingEpisodeId, x.p.Id, x.p.UserId, x.p.DisplayName, x.c.GoogleAttendeeSyncEnabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsInWindowAsync(
        DateTime fromUtc, DateTime toUtc, bool includeCancelled, CancellationToken cancellationToken)
    {
        var rows =
            from s in dbContext.CourseLiveSessions().AsNoTracking()
            join c in dbContext.Courses().AsNoTracking() on s.CourseId equals c.Id
            join p in dbContext.InstructorProfiles().AsNoTracking() on c.InstructorId equals p.Id
            select new { s, c, p };

        return await rows
            .Where(x => x.s.StartsAtUtc >= fromUtc && x.s.StartsAtUtc < toUtc
                && (includeCancelled || x.s.Status == CourseLiveSessionStatus.Scheduled))
            .OrderBy(x => x.s.StartsAtUtc).ThenBy(x => x.s.Id)
            .Take(LiveScheduleLimits.MaxWindowItems)
            .Select(x => ToContext(
                x.s.Id, x.s.CourseId, x.c.Title, x.c.Slug, x.s.Title, x.s.Description, x.s.StartsAtUtc, x.s.EndsAtUtc, x.s.Status,
                x.s.CancelReason, x.s.RecordingEpisodeId, x.p.Id, x.p.UserId, x.p.DisplayName, x.c.GoogleAttendeeSyncEnabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsForCoursesAsync(
        IReadOnlyCollection<Guid> courseIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        bool includeCancelled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(courseIds);

        var ids = courseIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var rows =
            from s in dbContext.CourseLiveSessions().AsNoTracking()
            join c in dbContext.Courses().AsNoTracking() on s.CourseId equals c.Id
            join p in dbContext.InstructorProfiles().AsNoTracking() on c.InstructorId equals p.Id
            select new { s, c, p };

        // Overlap window: a session that has started but not finished is still "in" the window.
        return await rows
            .Where(x => ids.Contains(x.s.CourseId)
                && (fromUtc == null || x.s.EndsAtUtc > fromUtc)
                && (toUtc == null || x.s.StartsAtUtc < toUtc)
                && (includeCancelled || x.s.Status == CourseLiveSessionStatus.Scheduled))
            .OrderBy(x => x.s.StartsAtUtc).ThenBy(x => x.s.Id)
            .Take(LiveScheduleLimits.MaxWindowItems)
            .Select(x => ToContext(
                x.s.Id, x.s.CourseId, x.c.Title, x.c.Slug, x.s.Title, x.s.Description, x.s.StartsAtUtc, x.s.EndsAtUtc, x.s.Status,
                x.s.CancelReason, x.s.RecordingEpisodeId, x.p.Id, x.p.UserId, x.p.DisplayName, x.c.GoogleAttendeeSyncEnabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LiveSessionContextPage> GetInstructorSessionContextsAsync(
        Guid instructorUserId,
        DateTime? fromUtc,
        DateTime? toUtc,
        bool includeCancelled,
        bool newestFirst,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var effectiveSkip = Math.Max(skip, 0);
        var effectiveTake = Math.Clamp(take, 1, LiveScheduleLimits.MaxPageSize);

        var filtered =
            from s in dbContext.CourseLiveSessions().AsNoTracking()
            join c in dbContext.Courses().AsNoTracking() on s.CourseId equals c.Id
            join p in dbContext.InstructorProfiles().AsNoTracking() on c.InstructorId equals p.Id
            where p.UserId == instructorUserId
                && (fromUtc == null || s.EndsAtUtc > fromUtc)
                && (toUtc == null || s.StartsAtUtc < toUtc)
                && (includeCancelled || s.Status == CourseLiveSessionStatus.Scheduled)
            select new { s, c, p };

        var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
        if (totalCount == 0 || effectiveSkip >= totalCount)
        {
            return new LiveSessionContextPage([], totalCount);
        }

        var ordered = newestFirst
            ? filtered.OrderByDescending(x => x.s.StartsAtUtc).ThenByDescending(x => x.s.Id)
            : filtered.OrderBy(x => x.s.StartsAtUtc).ThenBy(x => x.s.Id);

        var items = await ordered
            .Skip(effectiveSkip)
            .Take(effectiveTake)
            .Select(x => ToContext(
                x.s.Id, x.s.CourseId, x.c.Title, x.c.Slug, x.s.Title, x.s.Description, x.s.StartsAtUtc, x.s.EndsAtUtc, x.s.Status,
                x.s.CancelReason, x.s.RecordingEpisodeId, x.p.Id, x.p.UserId, x.p.DisplayName, x.c.GoogleAttendeeSyncEnabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new LiveSessionContextPage(items, totalCount);
    }

    internal static LiveSessionContext ToContext(
        Guid sessionId,
        Guid courseId,
        string courseTitle,
        string courseSlug,
        string title,
        string? description,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        CourseLiveSessionStatus status,
        string? cancelReason,
        Guid? recordingEpisodeId,
        Guid instructorProfileId,
        Guid instructorUserId,
        string instructorDisplayName,
        bool googleAttendeeSyncEnabled) => new(
        sessionId,
        courseId,
        courseTitle,
        courseSlug,
        // Defense in depth for rows stored before the write-side rule (CreateLiveSession/UpdateLiveSession/CancelLiveSession validators
        // reject a meeting-room link in these fields): every consumer — learner JSON, e-mails, the Google event — gets text without one.
        MeetingLinkText.ScrubRequired(title),
        MeetingLinkText.Scrub(description),
        startsAtUtc,
        endsAtUtc,
        status == CourseLiveSessionStatus.Cancelled ? LiveSessionStatus.Cancelled : LiveSessionStatus.Scheduled,
        MeetingLinkText.Scrub(cancelReason),
        recordingEpisodeId,
        instructorProfileId,
        instructorUserId,
        instructorDisplayName,
        googleAttendeeSyncEnabled);

    internal static LiveSessionInfo ToLiveSessionInfo(COURSE_LIVE_SESSION session) => new(
        session.Id,
        session.CourseId,
        MeetingLinkText.ScrubRequired(session.Title),
        session.StartsAtUtc,
        session.EndsAtUtc,
        session.Status == CourseLiveSessionStatus.Cancelled ? LiveSessionStatus.Cancelled : LiveSessionStatus.Scheduled,
        session.RecordingEpisodeId);
}
