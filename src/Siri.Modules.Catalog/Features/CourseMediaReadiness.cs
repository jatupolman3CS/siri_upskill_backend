using Siri.Modules.Catalog.Domain;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Catalog.Features;

/// <summary>
/// Deeper media-quality gate on top of <see cref="COURSE"/>'s own domain invariant (<c>CanPublishOrSubmit</c>) —
/// for whichever episodes a course does have, every one of them must have a media asset that is ready,
/// finished processing, and actually belongs to this instructor. This predates task P11-01 and was
/// always stricter than the domain method it backstops (domain only ever required "at least one" episode
/// with media; this required "every" episode).
/// <para>
/// P11-01 (docs/contracts/P11-01-catalog-live-sessions.md §2.4's "call sites that need fixing" note)
/// leaves the decision of whether this helper needs to become <see cref="Domain.DeliveryFormat"/>-aware
/// to the implementer — this is the resolution: a Live/Hybrid course relying purely on a future scheduled
/// live session (no episodes at all yet) must not be blocked by this gate's original "must have at least
/// one episode" requirement, since the course-level invariant it backstops no longer requires one either
/// in that case. Whatever episodes a course <em>does</em> have, though — regardless of format — must
/// still all be ready; this is a general quality bar, not something the live-session escape hatch should
/// weaken.
/// </para>
/// </summary>
internal static class CourseMediaReadiness
{
    public static async Task<Result> ValidateAsync(
        COURSE course, Guid ownerUserId, IMediaAssetContract mediaAssets, IClock clock, CancellationToken cancellationToken)
    {
        var episodes = course.Sections.SelectMany(section => section.Episodes).ToArray();

        // Same "session OR episode media" fallback COURSE's CanPublishOrSubmit uses — Live/Hybrid with a
        // qualifying future session does not need any episode at all to satisfy this gate's minimum-
        // presence check below.
        var hasQualifyingLiveSession = course.DeliveryFormat != DeliveryFormat.OnDemand
            && course.LiveSessions.Any(s => s.Status == CourseLiveSessionStatus.Scheduled && s.StartsAtUtc > clock.UtcNow);

        if (!hasQualifyingLiveSession && episodes.Length == 0)
        {
            return Result.Failure(DomainError.Validation("Every lesson must have a ready video before submitting or publishing the course."));
        }

        if (episodes.Any(episode => episode.MediaAssetId is null))
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
