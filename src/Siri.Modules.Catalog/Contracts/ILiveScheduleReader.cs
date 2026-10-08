using System.Linq;

namespace Siri.Modules.Catalog.Contracts;

public sealed record LiveSessionInfo(
    Guid SessionId,
    Guid CourseId,
    string Title,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    LiveSessionStatus Status,
    Guid? RecordingEpisodeId);

/// <summary>
/// A live session joined with the course/instructor facts the Live module needs (task P11-03,
/// docs/contracts/P11-03-live-module-google-meetings.md §4.1) — so Live never reaches into Catalog's
/// Domain/Infrastructure to learn who teaches a session or what its course is called.
/// </summary>
/// <param name="InstructorProfileId"><c>CATALOG.INSTRUCTOR_PROFILES.Id</c> of the owning instructor.</param>
/// <param name="InstructorUserId"><c>identity.Users.Id</c> of the owning instructor — the key Live uses for the
/// instructor's connected Google account.</param>
/// <param name="GoogleAttendeeSyncEnabled">The course's opt-in to invite learners as Google Calendar attendees
/// (<c>COURSES.GOOGLE_ATTENDEE_SYNC_ENABLED</c>, P11-04).</param>
public sealed record LiveSessionContext(
    Guid SessionId,
    Guid CourseId,
    string CourseTitle,
    string CourseSlug,
    string Title,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    LiveSessionStatus Status,
    string? CancelReason,
    Guid? RecordingEpisodeId,
    Guid InstructorProfileId,
    Guid InstructorUserId,
    string InstructorDisplayName,
    bool GoogleAttendeeSyncEnabled);

public sealed record LiveSessionContextPage(IReadOnlyList<LiveSessionContext> Items, int TotalCount);

/// <summary>Hard caps the <see cref="ILiveScheduleReader"/> context queries apply regardless of what a caller asks for.</summary>
public static class LiveScheduleLimits
{
    /// <summary>Largest page <see cref="ILiveScheduleReader.GetInstructorSessionContextsAsync"/> returns.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Largest result of a window / per-course context query.</summary>
    public const int MaxWindowItems = 1000;
}

/// <summary>Read-side ของตารางสอนสด — implement โดย Catalog เอง (ต่างจาก ILiveMeetingSink ที่ Catalog
/// แค่ประกาศ). ผู้บริโภค: Siri.Modules.Live (P11-03's live-meeting-sync job อ่าน session เดียวตอน sync,
/// P11-04's live-invite-reconcile job อ่าน upcoming ทั้งชุดตอน diff invite) และ Learning (P12's
/// study-plan อ่าน "คาบถัดไป").</summary>
public interface ILiveScheduleReader
{
    Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken);

    /// <summary>เฉพาะ Status == Scheduled ในช่วง [fromUtc, toUtc) — คาบ Cancelled ไม่นับเป็น "upcoming"
    /// (ไม่มีอะไรให้ reconcile invite ต่อ, การยกเลิก invite ของคาบที่ถูก cancel เป็นเส้นทาง reactive แยก
    /// ผ่าน ILiveMeetingSink.OnSessionCancelledAsync ไม่ใช่เส้นทางนี้).</summary>
    Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Earliest <see cref="LiveSessionStatus.Scheduled"/> session for <paramref name="courseId"/>
    /// by <see cref="LiveSessionInfo.StartsAtUtc"/>, or <c>null</c> if none exists (task P11-13, Q13.3 —
    /// docs/contracts/P11-13-access-duration-first-session.md). <c>null</c> covers two cases the caller
    /// must NOT try to tell apart: the course is <c>DeliveryFormat.OnDemand</c> (which can never have a
    /// Scheduled session — see <c>COURSE.AddLiveSession</c>/<c>SetDeliveryFormat</c>'s own guards), or it's
    /// Live/Hybrid but no session has been scheduled yet. Both map to the exact same Q13.3 fallback
    /// ("count access from purchase date"), so the caller needs nothing more specific than this.
    /// <para>
    /// Default method built directly on <see cref="GetSessionsForCourseAsync"/> instead of a new EF query
    /// in <c>LiveScheduleReader</c> — no other consumer needs anything more targeted than "all sessions for
    /// this course, filtered/sorted in memory", and per-course session counts are small (a handful, not
    /// thousands), so the extra round trip through the full list is not a real cost.
    /// </para></summary>
    async Task<LiveSessionInfo?> GetEarliestScheduledSessionAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var sessions = await GetSessionsForCourseAsync(courseId, cancellationToken).ConfigureAwait(false);
        return sessions
            .Where(s => s.Status == LiveSessionStatus.Scheduled)
            .OrderBy(s => s.StartsAtUtc)
            .FirstOrDefault();
    }

    /// <summary>Contexts for exactly <paramref name="sessionIds"/> (any status, including
    /// <see cref="LiveSessionStatus.Cancelled"/>); ids that do not exist — or whose course was soft-deleted — are simply
    /// absent from the result. Default: empty (an implementer that predates P11-03 reports nothing).</summary>
    Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LiveSessionContext>>([]);

    /// <summary>Sessions whose <c>StartsAtUtc</c> falls in <c>[fromUtc, toUtc)</c> (a <em>start</em> window — used by the
    /// invite/reminder jobs), ordered by start time. <paramref name="includeCancelled"/> <c>false</c> keeps only
    /// <see cref="LiveSessionStatus.Scheduled"/> sessions. The result is capped at
    /// <see cref="LiveScheduleLimits.MaxWindowItems"/> rows. Default: empty.</summary>
    Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsInWindowAsync(
        DateTime fromUtc,
        DateTime toUtc,
        bool includeCancelled,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LiveSessionContext>>([]);

    /// <summary>Sessions of <paramref name="courseIds"/> that <em>overlap</em> <c>[fromUtc, toUtc)</c>
    /// (<c>EndsAtUtc &gt; fromUtc &amp;&amp; StartsAtUtc &lt; toUtc</c>; a <c>null</c> bound means unbounded on that side), so a
    /// session that is in progress is included. Ordered by start time and capped at
    /// <see cref="LiveScheduleLimits.MaxWindowItems"/>. Default: empty.</summary>
    Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsForCoursesAsync(
        IReadOnlyCollection<Guid> courseIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        bool includeCancelled,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LiveSessionContext>>([]);

    /// <summary>One page of an instructor's sessions across all of their courses, overlapping
    /// <c>[fromUtc, toUtc)</c> with the same overlap semantics as <see cref="GetSessionContextsForCoursesAsync"/>.
    /// <paramref name="take"/> is clamped to <see cref="LiveScheduleLimits.MaxPageSize"/>; <paramref name="skip"/> to &gt;= 0.
    /// Default: an empty page.</summary>
    Task<LiveSessionContextPage> GetInstructorSessionContextsAsync(
        Guid instructorUserId,
        DateTime? fromUtc,
        DateTime? toUtc,
        bool includeCancelled,
        bool newestFirst,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        Task.FromResult(new LiveSessionContextPage([], 0));
}
