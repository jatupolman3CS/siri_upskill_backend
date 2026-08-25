using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.CreateCourse;

public sealed record CreateCourseResponse(
    Guid Id,
    string Slug,
    string Title,
    Guid InstructorId,
    Guid CategoryId,
    CourseLevel Level,
    CourseLanguage Language,
    decimal Price,
    string Currency,
    CourseStatus Status);
