namespace Siri.Modules.Catalog.Features.AutosaveCourse;

public sealed record AutosaveCourseResponse(
    Guid CourseId,
    byte[] RowVersion,
    DateTime UpdatedAtUtc);
