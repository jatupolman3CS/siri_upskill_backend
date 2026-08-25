using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.GetCourse;

public sealed record CourseResponse(
    Guid Id,
    string Slug,
    string Title,
    string? Subtitle,
    string? Description,
    Guid InstructorId,
    Guid CategoryId,
    CourseLevel Level,
    CourseLanguage Language,
    string? ThumbnailUrl,
    decimal Price,
    decimal? ComparePrice,
    string Currency,
    int? AccessDurationDays,
    CourseStatus Status,
    string? SeoTitle,
    string? SeoDescription);
