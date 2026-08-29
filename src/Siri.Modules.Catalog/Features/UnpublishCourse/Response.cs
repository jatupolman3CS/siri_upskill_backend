using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.UnpublishCourse;

public sealed record UnpublishCourseResponse(Guid Id, CourseStatus Status, string Reason);
