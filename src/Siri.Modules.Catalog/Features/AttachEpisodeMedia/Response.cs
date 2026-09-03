using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.AttachEpisodeMedia;

public sealed record EpisodeMediaResponse(
    Guid EpisodeId,
    Guid SectionId,
    Guid CourseId,
    Guid? MediaAssetId,
    int? DurationSeconds,
    CourseEpisodeStatus Status);
