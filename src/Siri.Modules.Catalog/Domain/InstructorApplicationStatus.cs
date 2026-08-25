namespace Siri.Modules.Catalog.Domain;

/// <summary>docs/DATABASE.md's catalog section: "InstructorProfiles(... Status, ApprovedAtUtc)".
/// The admin-review workflow itself (task P1-03) is a single Pending→Approved/Rejected step — nothing
/// like Course's multi-stage Draft/InReview/Archived states, since applying to become an instructor has
/// no draft/in-progress concept of its own.</summary>
public enum InstructorApplicationStatus
{
    Pending,
    Approved,
    Rejected,
}
