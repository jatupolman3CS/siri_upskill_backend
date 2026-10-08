using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Media.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Infrastructure.Seeding;

/// <summary>
/// Dev tooling only (explicit <c>--seed</c> CLI or the Development-only <c>SIRI_DEV_SAMPLE_VIDEO</c>
/// startup hook): seeds a Ready MEDIA_ASSET row for one real video in the project's own Bunny Stream
/// library and attaches it to sample course episodes. There is no mock/fake video — playing it still
/// needs real Bunny credentials, otherwise the API answers 503 <c>video.provider_not_configured</c>.
/// </summary>
public sealed class MediaSeeder(AppDbContext dbContext, IClock clock, ILogger<MediaSeeder> logger)
{
    public const string DefaultBunnyVideoId = "448944e4-c1bd-4f61-a8b1-3e10e469aa69";

    /// <summary>
    /// Ensures that a Ready MEDIA_ASSET row exists for the real Bunny video ID and associates it with
    /// course episodes in the catalog that have no (or a dangling) media asset.
    /// </summary>
    public async Task<Guid> SeedAsync(Guid uploaderUserId, CancellationToken cancellationToken)
    {
        // Seed the real Bunny Video Asset
        var existingAsset = await dbContext.MediaAssets()
            .FirstOrDefaultAsync(m => m.PROVIDER == "BunnyStream" && m.PROVIDER_ASSET_ID == DefaultBunnyVideoId, cancellationToken)
            .ConfigureAwait(false);

        Guid assetId;

        if (existingAsset is not null)
        {
            if (existingAsset.STATUS != MediaAssetStatus.Ready)
            {
                existingAsset.MarkReady(DefaultBunnyVideoId, durationSeconds: 600, thumbnailUrl: null, clock);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            assetId = existingAsset.MEDIA_ASSET_ID;
        }
        else
        {
            var newAsset = MEDIA_ASSET.Create("BunnyStream", DefaultBunnyVideoId, uploaderUserId, drmEnabled: true);
            newAsset.MarkReady(DefaultBunnyVideoId, durationSeconds: 600, thumbnailUrl: null, clock);
            dbContext.MediaAssets().Add(newAsset);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            assetId = newAsset.MEDIA_ASSET_ID;

            logger.LogInformation("Seed: Created Bunny Stream media asset {AssetId} for video {VideoId}.",
                assetId, DefaultBunnyVideoId);
        }

        // Link all episodes with missing or orphan media asset IDs to this verified Bunny video asset
        try
        {
            var rowsUpdated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                // Every identifier is double-quoted (P0-41): the whole schema is UPPERCASE and
                // PostgreSQL folds unquoted identifiers to lower case, so bare CATALOG.COURSE_EPISODES
                // would resolve to a non-existent catalog.course_episodes.
                $@"UPDATE ""CATALOG"".""COURSE_EPISODES""
                   SET ""MEDIA_ASSET_ID"" = {assetId}
                   WHERE ""MEDIA_ASSET_ID"" IS NULL
                      OR ""MEDIA_ASSET_ID"" NOT IN (SELECT ""MEDIA_ASSET_ID"" FROM ""MEDIA"".""MEDIA_ASSETS"")",
                cancellationToken).ConfigureAwait(false);

            if (rowsUpdated > 0)
            {
                logger.LogInformation("Seed: Attached Bunny video asset {AssetId} to {Count} course episode(s).",
                    assetId, rowsUpdated);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Seed: Note while updating episode media associations: {Message}", ex.Message);
        }

        return assetId;
    }
}
