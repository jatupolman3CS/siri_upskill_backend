using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Media.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Infrastructure.Seeding;

/// <summary>
/// Seeds sample / default Bunny Stream media assets and attaches them to sample course episodes.
/// </summary>
public sealed class MediaSeeder(AppDbContext dbContext, IClock clock, ILogger<MediaSeeder> logger)
{
    public const string DefaultBunnyVideoId = "448944e4-c1bd-4f61-a8b1-3e10e469aa69";
    public const string MockVideoId = "mock-video-demo-1";

    /// <summary>
    /// Ensures that Ready MEDIA_ASSET rows exist for both the Bunny video ID and the mock video asset,
    /// and associates them with course episodes in the catalog.
    /// </summary>
    public async Task<Guid> SeedAsync(Guid uploaderUserId, CancellationToken cancellationToken)
    {
        // 1. Seed Bunny Video Asset
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

        // 2. Seed Mock Video Demo Asset
        var existingMockAsset = await dbContext.MediaAssets()
            .FirstOrDefaultAsync(m => m.PROVIDER == "BunnyStream" && m.PROVIDER_ASSET_ID == MockVideoId, cancellationToken)
            .ConfigureAwait(false);

        if (existingMockAsset is null)
        {
            var mockAsset = MEDIA_ASSET.Create("BunnyStream", MockVideoId, uploaderUserId, drmEnabled: true);
            mockAsset.MarkReady(MockVideoId, durationSeconds: 596, thumbnailUrl: null, clock);
            dbContext.MediaAssets().Add(mockAsset);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Seed: Created Mock Video demo asset {AssetId}.", mockAsset.MEDIA_ASSET_ID);
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
