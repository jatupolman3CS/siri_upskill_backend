using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.CreateCourse;

/// <summary>Request payload for POST /api/catalog/instructor/courses. Binds from the JSON request body —
/// deliberately carries no <c>Slug</c> (generated — see <c>CreateCourseHandler</c>) and no
/// <c>InstructorId</c> (resolved from the caller's own <c>IUserContext.UserId</c>, never accepted from
/// the client — backend.md's "userId มาจาก IUserContext เท่านั้น" extended to instructor identity).</summary>
public sealed record CreateCourseCommand(string Title, Guid CategoryId, CourseLevel Level, CourseLanguage Language, decimal Price);
