namespace Siri.Modules.Catalog.Features.RejectCourse;

/// <summary>Request payload for POST /api/catalog/admin/courses/{id}/reject. A reason is required — task
/// P1-05's "validation rule + audit" implies the instructor needs to know what to fix, not just that
/// their course was rejected.</summary>
public sealed record RejectCourseCommand(string Reason);
