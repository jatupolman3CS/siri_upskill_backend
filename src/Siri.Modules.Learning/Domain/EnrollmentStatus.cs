namespace Siri.Modules.Learning.Domain;

/// <summary>docs/DATABASE.md's "learning" section: "Status" on Enrollments — <see cref="Active"/> while
/// access is current, <see cref="Expired"/> once <see cref="ENROLLMENT.EXPIRES_AT_UTC"/> passes (a later
/// task's scheduled job, not this scaffold's), <see cref="Revoked"/> for an administrative/refund-driven
/// removal of access. No "Cancelled" value — an enrollment that never actually started is a Commerce/Order-
/// level concept (<c>Orders.Status</c>), not this table's.</summary>
public enum EnrollmentStatus
{
    Active,
    Expired,
    Revoked,
}
