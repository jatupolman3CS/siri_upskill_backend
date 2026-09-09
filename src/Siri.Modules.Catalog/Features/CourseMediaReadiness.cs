using Siri.Modules.Catalog.Domain;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Catalog.Features;

internal static class CourseMediaReadiness
{
    public static async Task<Result> ValidateAsync(
        COURSE course, Guid ownerUserId, IMediaAssetContract mediaAssets, CancellationToken cancellationToken)
    {
        var episodes = course.Sections.SelectMany(section => section.Episodes).ToArray();
        if (episodes.Length == 0 || episodes.Any(episode => episode.MediaAssetId is null))
        {
            return Result.Failure(DomainError.Validation("Every lesson must have a ready video before submitting or publishing the course."));
        }

        var ids = episodes.Select(episode => episode.MediaAssetId!.Value)
            .Concat(course.TrailerMediaAssetId is { } trailerId ? [trailerId] : [])
            .Distinct();
        foreach (var id in ids)
        {
            var asset = await mediaAssets.GetAssetSummaryAsync(id, cancellationToken).ConfigureAwait(false);
            if (asset is null || asset.UploadedByUserId != ownerUserId || asset.Status != "Ready" || asset.DurationSeconds is not > 0)
            {
                return Result.Failure(DomainError.Validation("All lesson videos and the selected trailer must belong to this instructor and finish processing before submitting or publishing."));
            }
        }

        return Result.Success();
    }
}
