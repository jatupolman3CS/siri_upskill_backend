using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.RejectCourse;

public sealed record RejectCourseResponse(Guid Id, CourseStatus Status, string RejectionReason);
