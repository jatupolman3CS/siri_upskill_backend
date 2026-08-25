using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Data access for <see cref="ENROLLMENT"/>, consumed by <see cref="EnrollmentService"/>. Interface
/// name/members are NOT uppercased — D-17's UPPERCASE naming exception is entity classes/properties and
/// DB tables/columns only, not Repository/Service/DTO/interface names (see <see cref="ENROLLMENT"/>'s own
/// doc comment).
/// </summary>
public interface IEnrollmentRepository
{
    /// <summary>Tracked lookup by primary key — <c>null</c> if no such row exists.</summary>
    Task<ENROLLMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked lookup by the unique (UserId, CourseId) pair — supports idempotent creation (does
    /// this learner already have an enrollment for this course?), same role
    /// <c>IRevenueSplitRepository.GetByOrderItemIdAsync</c>'s own doc comment describes for its own unique
    /// key.</summary>
    Task<ENROLLMENT?> GetByUserAndCourseAsync(Guid userId, Guid courseId, CancellationToken cancellationToken);

    /// <summary>Untracked (<c>AsNoTracking</c>) query source for read scenarios — the caller composes its
    /// own filtering/paging/projection (database.md: "Projection ไปเป็น DTO ตรง ๆ ดีกว่าดึง entity มาทั้งก้อน
    /// แล้ว map").</summary>
    IQueryable<ENROLLMENT> Query();

    /// <summary>Stages a new row for insertion — does not persist until <see cref="SaveChangesAsync"/>.
    /// </summary>
    void Add(ENROLLMENT enrollment);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
