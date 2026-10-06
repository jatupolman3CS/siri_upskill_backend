namespace Siri.Modules.Catalog.Contracts;

/// <summary>
/// <paramref name="AccessDurationDays"/> mirrors <c>COURSE.AccessDurationDays</c> — <c>null</c> means
/// lifetime access. Callers granting an enrollment (<c>Siri.Modules.Learning.Contracts
/// .ILearningAccessContract.EnrollUserAsync</c>) must use this to compute the enrollment's real
/// <c>EXPIRES_AT_UTC</c> — treating every purchase as lifetime access regardless of the course's actual
/// setting silently grants more than what was paid for.
/// </summary>
public sealed record CoursePriceInfo(Guid CourseId, string Title, decimal Price, Guid InstructorId, int? AccessDurationDays);

public sealed record CourseEpisodeInfo(Guid EpisodeId, Guid CourseId, string Title, int SortOrder);

/// <summary>Task P11-11 (Q13.1/Q13.2 — docs/contracts/P11-11-enrollment-deadline-seat-cap.md §2.4).
/// <paramref name="EnrollmentDeadlineUtc"/> <c>null</c> = never closes; <paramref name="MaxSeats"/>
/// <c>null</c> = unlimited.</summary>
public sealed record CourseEnrollmentPolicyInfo(Guid CourseId, DateTime? EnrollmentDeadlineUtc, int? MaxSeats, int SeatsUsed);

public interface ICatalogPriceContract
{
    Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken);

    Task<bool> IsEpisodeFreePreviewAsync(
        Guid episodeId,
        CancellationToken cancellationToken);

    Task<Guid?> GetCourseIdForEpisodeAsync(
        Guid episodeId,
        CancellationToken cancellationToken);

    Task<Guid?> GetMediaAssetIdForEpisodeAsync(
        Guid episodeId,
        CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

    /// <summary>
    /// True when <paramref name="instructorUserId"/> (an <c>identity.Users.Id</c>, e.g. from
    /// <c>IUserContext</c>) is the approved instructor owning the course that <paramref name="episodeId"/>
    /// belongs to.
    /// </summary>
    Task<bool> IsInstructorOwnerOfEpisodeAsync(
        Guid episodeId,
        Guid instructorUserId,
        CancellationToken cancellationToken);

    Task<bool> IsInstructorOwnerOfCourseAsync(
        Guid courseId,
        Guid instructorUserId,
        CancellationToken cancellationToken);

    Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(
        IEnumerable<Guid> instructorIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetCourseIdsByInstructorUserIdAsync(
        Guid instructorUserId,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

    Task<IReadOnlyList<CourseEpisodeInfo>> GetEpisodesForCoursesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CourseEpisodeInfo>>(Array.Empty<CourseEpisodeInfo>());

    /// <summary>อ่านนโยบายปิดรับสมัคร/เพดานที่นั่งของ courseIds ที่ระบุ (task P11-11) — คืนเฉพาะ courseId ที่มีอยู่จริง
    /// (ไม่ throw ถ้าบาง id ไม่พบ — ผู้เรียก เช่น OrderService ต้องพึ่งจุดอื่นที่เช็ค "คอร์สมีอยู่จริงและ Published"
    /// อยู่แล้ว เช่น GetPublishedCoursePricesAsync's count check ใน PricingEngine, method นี้ไม่ทำซ้ำ). Default:
    /// dictionary ว่าง (= ไม่มีนโยบายอะไรเลยสำหรับทุกคอร์ส, ปลอดภัยสำหรับ implementer เดิมที่ไม่รู้จัก method
    /// นี้).</summary>
    Task<IReadOnlyDictionary<Guid, CourseEnrollmentPolicyInfo>> GetEnrollmentPoliciesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CourseEnrollmentPolicyInfo>>(new Dictionary<Guid, CourseEnrollmentPolicyInfo>());

    /// <summary>
    /// จองที่นั่งแบบ atomic ที่ระดับ SQL หนึ่งที่นั่งบน courseId (task P11-11) — SQL เดียวกับ pattern ที่มีอยู่แล้วจริง
    /// ใน PromoCodeRepository.TryRedeemAsync's global-quota step (ExecuteUpdateAsync + WHERE MaxSeats IS NULL
    /// OR SeatsUsed &lt; MaxSeats แล้วเช็ค affected rows): เมื่อเรียกจากภายใน IOrderRepository
    /// .ExecuteInTransactionAsync การจองและการสร้าง order จะ atomic ร่วมกัน (rollback พร้อมกันถ้าขั้นตอนอื่นใน
    /// ธุรกรรมเดียวกันล้มเหลวทีหลัง). คืน true = จองสำเร็จ (นับรวมกรณี MaxSeats เป็น null ซึ่งจองสำเร็จเสมอแต่
    /// SeatsUsed ยังถูกนับเพิ่มไว้จริง เผื่อวันหลังผู้สอนเพิ่งมาตั้ง MaxSeats ทีหลัง ตัวนับจะสะท้อนของจริง ไม่ใช่เริ่มนับ
    /// จาก 0 ใหม่). คืน false = เต็มแล้ว (หรือ courseId ไม่มีอยู่จริง). Default: true เสมอ (ปลอดภัยสำหรับ
    /// implementer เดิม — เท่ากับไม่มีการจำกัดที่นั่ง).
    /// </summary>
    Task<bool> TryReserveSeatAsync(Guid courseId, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    /// <summary>คืนที่นั่งหนึ่งที่ (task P11-11 — SeatsUsed - 1, floor ที่ 0 ด้วย WHERE SeatsUsed &gt; 0 — ไม่มีทาง
    /// ติดลบ) — เรียกคู่กับทุกจุดที่ order ที่เคยเรียก TryReserveSeatAsync สำเร็จถูกยกเลิก/หมดอายุ**ก่อนจ่ายเงิน**
    /// เท่านั้น (refund หลังจ่ายเงินไม่เรียก method นี้ — docs/contracts/
    /// P11-11-enrollment-deadline-seat-cap.md §4.5). ปลอดภัยเรียกซ้ำ/เรียกกับคอร์สที่ไม่มี MaxSeats (no-op
    /// ทั้งคู่). Default: no-op (ปลอดภัยสำหรับ implementer เดิม).</summary>
    Task ReleaseSeatAsync(Guid courseId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
