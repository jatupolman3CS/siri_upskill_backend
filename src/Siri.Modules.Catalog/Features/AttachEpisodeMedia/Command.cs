namespace Siri.Modules.Catalog.Features.AttachEpisodeMedia;

public sealed record AttachEpisodeMediaCommand(Guid MediaAssetId, int? DurationSeconds = null);
