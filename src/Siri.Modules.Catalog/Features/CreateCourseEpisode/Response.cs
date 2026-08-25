using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.CreateCourseEpisode;

public sealed record CourseEpisodeResponse(
    Guid Id,
    Guid CourseId,
    Guid SectionId,
    string Title,
    string? Description,
    int SortOrder,
    bool IsFreePreview,
    Guid? MediaAssetId,
    int? DurationSeconds,
    CourseEpisodeStatus Status);
