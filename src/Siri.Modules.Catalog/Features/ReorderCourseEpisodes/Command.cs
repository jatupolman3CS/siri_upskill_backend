namespace Siri.Modules.Catalog.Features.ReorderCourseEpisodes;

public sealed record ReorderCourseEpisodesCommand(IReadOnlyList<ReorderCourseEpisodeItem> Items);

public sealed record ReorderCourseEpisodeItem(Guid EpisodeId, int SortOrder);
