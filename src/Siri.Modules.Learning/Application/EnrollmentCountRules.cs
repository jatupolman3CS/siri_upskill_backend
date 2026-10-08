using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// When an enrollment starts or stops counting toward <c>COURSES.ENROLLMENT_COUNT</c> (Catalog's <c>ICourseEnrollmentCountUpdater</c>) — the one place that
/// knows the definition on Learning's side: an enrollment counts while its status is <see cref="EnrollmentStatus.Active"/> or <see cref="EnrollmentStatus.Expired"/>
/// (everyone who has the course and was not revoked — the same rule the instructor dashboard's <c>totalStudents</c> KPI uses). Only a <b>revoked</b> enrollment
/// does not count, so:
/// <list type="bullet">
/// <item><description>a brand-new enrollment: +1;</description></item>
/// <item><description>reactivating a <c>Revoked</c> one (the learner buys again): +1; reactivating an <c>Expired</c> or still-<c>Active</c> one: 0 — it already counted;</description></item>
/// <item><description>revoking an <c>Active</c> or <c>Expired</c> one: -1; revoking one that is already <c>Revoked</c>: 0 (idempotent);</description></item>
/// <item><description><c>Active</c> to <c>Expired</c>: 0 — an access that merely lapsed still counts.</description></item>
/// </list>
/// A delta is applied only for a real transition, so a retried request (a webhook delivered twice) cannot count twice.
/// </summary>
public static class EnrollmentCountRules
{
    /// <summary>Whether an enrollment in <paramref name="status"/> is part of the course's enrollment count.</summary>
    public static bool Counts(EnrollmentStatus status) => status != EnrollmentStatus.Revoked;

    /// <summary>Delta for granting access: <paramref name="previousStatus"/> is <c>null</c> when a new enrollment row is created, else the status the existing row had before it was reactivated.</summary>
    public static int DeltaForGrant(EnrollmentStatus? previousStatus) => previousStatus is { } previous && Counts(previous) ? 0 : 1;

    /// <summary>Delta for revoking an enrollment that was in <paramref name="previousStatus"/>.</summary>
    public static int DeltaForRevoke(EnrollmentStatus previousStatus) => Counts(previousStatus) ? -1 : 0;

    /// <summary>Reports <paramref name="delta"/> to Catalog — nothing at all when it is zero (the enrollment did not start or stop counting), so an idempotent retry costs no database call.</summary>
    public static Task ReportAsync(ICourseEnrollmentCountUpdater updater, Guid courseId, int delta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updater);

        return delta == 0 ? Task.CompletedTask : updater.AdjustAsync(courseId, delta, cancellationToken);
    }
}
