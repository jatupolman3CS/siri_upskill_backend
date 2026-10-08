namespace Siri.Modules.Catalog.Contracts;

/// <summary>
/// The <b>single writer</b> of <c>COURSES.ENROLLMENT_COUNT</c> (database.md: a denormalized counter is written from exactly one updater, never from several
/// handlers) — the "N learners" shown on public cards / course detail and in the instructor dashboard's per-course table. Implemented by Catalog (it owns the
/// column); called by Learning (it owns the enrollments) when an enrollment starts to count or stops counting, and by the hourly <c>course-enrollment-recount</c>
/// job to heal drift and fill in rows that existed before the counter had a writer.
/// <para>
/// <b>Definition of "enrollment count":</b> the number of enrollments of the course whose status is <c>Active</c> or <c>Expired</c> — everyone who bought/was granted
/// the course and was not revoked. The same rule the instructor dashboard's <c>totalStudents</c> KPI uses (<c>LearningAnalyticsContract.CountDistinctLearnersAsync</c>:
/// "a Revoked one is not a learner"); <c>UNIQUE (USER_ID, COURSE_ID)</c> makes rows and distinct learners the same number for one course. An access that merely
/// lapsed (<c>Expired</c>) still counts: they did learn here, and the number is social proof, not a seat count.
/// </para>
/// </summary>
public interface ICourseEnrollmentCountUpdater
{
    /// <summary>
    /// Adds <paramref name="delta"/> (+1 when an enrollment is created or comes back from <c>Revoked</c>, -1 when one is revoked) to the course's counter in ONE
    /// atomic SQL statement — correct under any number of concurrent enrollments, never below zero, a no-op for an unknown/deleted course and for <c>0</c>. It is a
    /// plain <c>ExecuteUpdate</c>, so it takes part in the caller's ambient transaction (a payment fulfilment keeps the count and the enrollment atomic) and needs no
    /// <c>SaveChanges</c>. Callers must apply a delta only for a real status transition so a retried request cannot count twice.
    /// </summary>
    Task AdjustAsync(Guid courseId, int delta, CancellationToken cancellationToken);

    /// <summary>
    /// Recomputes one course's counter from the source of truth (<see cref="ILearningEnrollmentCounter"/>) and stores it, returning the stored value (<c>0</c> for an
    /// unknown/deleted course). The course row is locked first and the count is taken after the lock is held, so concurrent <see cref="AdjustAsync"/> calls and other
    /// recounts cannot interleave and leave a stale value; idempotent. Opens its own transaction unless the caller already has one.
    /// </summary>
    Task<int> RecountAsync(Guid courseId, CancellationToken cancellationToken);

    /// <summary>
    /// Walks every course in pages of <paramref name="pageSize"/>, compares the stored counter with the source of truth (one grouped query per page) and
    /// <see cref="RecountAsync"/>s only the ones that differ — so historical rows become correct without a data migration and drift never lasts more than one run.
    /// Returns how many courses were corrected. Safe to run repeatedly and concurrently with enrollments.
    /// </summary>
    Task<int> RecountDriftedAsync(int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// What Catalog needs from Learning to recount (Catalog never reads Learning's tables): how many enrollments of each course count toward
/// <c>ENROLLMENT_COUNT</c> (see <see cref="ICourseEnrollmentCountUpdater"/> for the definition). Implemented by Learning.
/// </summary>
public interface ILearningEnrollmentCounter
{
    /// <summary>One entry per distinct non-empty id in <paramref name="courseIds"/> — <c>0</c> for a course nobody is enrolled in.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountEnrollmentsAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken);
}
