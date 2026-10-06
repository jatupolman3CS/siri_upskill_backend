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
}
