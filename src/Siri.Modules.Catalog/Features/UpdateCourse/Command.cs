using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.UpdateCourse;

/// <summary>Request payload for PUT /api/catalog/instructor/courses/{id}. Full-replace semantics for
/// every field it covers, same PUT style <c>UpdateCategoryCommand</c> already uses — the client resends
/// the complete set of editable fields each time. No <c>Slug</c> field: immutable after creation in this
/// task's scope (see <c>UpdateCourseHandler</c>'s own doc comment). No <c>TrailerMediaAssetId</c>: no
/// upload flow exists yet to produce a valid one (same forward-reference gap <c>Course.cs</c>'s own doc
/// comment already names).</summary>
public sealed record UpdateCourseCommand(
    string Title,
    string? Subtitle,
    string? Description,
    Guid CategoryId,
    CourseLevel Level,
    CourseLanguage Language,
    string? ThumbnailUrl,
    decimal Price,
    decimal? ComparePrice,
    int? AccessDurationDays,
    string? SeoTitle,
    string? SeoDescription);
