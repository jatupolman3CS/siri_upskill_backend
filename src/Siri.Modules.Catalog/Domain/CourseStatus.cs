namespace Siri.Modules.Catalog.Domain;

/// <summary>docs/DATABASE.md's catalog section: "Status — Draft|InReview|Published|Archived|Rejected".
/// The Draft→InReview→Published/Rejected admin-approval workflow itself is task P1-05's — this task
/// (P1-02) only defines the states and <see cref="COURSE.Publish"/>'s own guard.</summary>
public enum CourseStatus
{
    Draft,
    InReview,
    Published,
    Archived,
    Rejected,
}
