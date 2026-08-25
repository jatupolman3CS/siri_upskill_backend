using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.ApproveCourse;

public sealed record ApproveCourseResponse(Guid Id, CourseStatus Status, DateTime? PublishedAtUtc);
