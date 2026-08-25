namespace Siri.Modules.Catalog.Features.CreateCourseSection;

public sealed record CourseSectionResponse(Guid Id, Guid CourseId, string Title, int SortOrder);
