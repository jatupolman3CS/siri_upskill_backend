using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.AutosaveCourse;

public sealed record AutosaveCourseCommand(
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
    string? SeoDescription,
    IReadOnlyList<string>? Outcomes,
    IReadOnlyList<string>? Requirements,
    IReadOnlyList<AutosaveSectionItem>? Sections,
    byte[] RowVersion,
    Guid? TrailerMediaAssetId = null);

public sealed record AutosaveSectionItem(
    Guid? Id,
    string Title,
    int SortOrder,
    IReadOnlyList<AutosaveEpisodeItem>? Episodes);

public sealed record AutosaveEpisodeItem(
    Guid? Id,
    string Title,
    string? Description,
    int SortOrder,
    bool IsFreePreview,
    Guid? MediaAssetId = null,
    int? DurationSeconds = null);
