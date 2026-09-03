using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.GetCourseBuilder;

public sealed record CourseBuilderResponse(
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
    string? SeoDescription,
    byte[] RowVersion,
    IReadOnlyList<string> Outcomes,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<CourseBuilderSectionResponse> Sections,
    Guid? TrailerMediaAssetId = null);

public sealed record CourseBuilderSectionResponse(
    Guid Id,
    string Title,
    int SortOrder,
    IReadOnlyList<CourseBuilderEpisodeResponse> Episodes);

public sealed record CourseBuilderEpisodeResponse(
    Guid Id,
    Guid SectionId,
    string Title,
    string? Description,
    int SortOrder,
    bool IsFreePreview,
    Guid? MediaAssetId,
    int? DurationSeconds,
    CourseEpisodeStatus Status);
