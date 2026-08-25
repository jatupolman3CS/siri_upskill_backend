using Microsoft.EntityFrameworkCore;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.Persistence;

namespace Siri.Modules.Media.Infrastructure;

/// <summary>
/// EF Core-backed <see cref="IMediaAssetRepository"/>. Pure data access — no business logic — so (unlike
/// the Domain/Application layers in this scaffold pass, see <see cref="MEDIA_ASSET"/>'s own doc comment)
/// these query implementations are real, not stubbed: there is no judgment call in "fetch this row"/"list
/// these rows"/"stage this change" for a later task to make differently.
/// </summary>
public sealed class MediaAssetRepository(AppDbContext dbContext) : IMediaAssetRepository
{
    /// <summary>Tracked (no <c>AsNoTracking</c>) — callers use this to load an entity they intend to mutate
    /// and save, same reasoning <c>Siri.Modules.Catalog.Features.DeleteCourse.DeleteCourseHandler</c>'s own
    /// lookup query gives.</summary>
    public Task<MEDIA_ASSET?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MediaAssets().FirstOrDefaultAsync(a => a.MEDIA_ASSET_ID == id, cancellationToken);

    public Task<MEDIA_ASSET?> GetByProviderAssetIdAsync(string providerAssetId, CancellationToken cancellationToken) =>
        dbContext.MediaAssets().FirstOrDefaultAsync(a => a.PROVIDER_ASSET_ID == providerAssetId, cancellationToken);

    public async Task<(IReadOnlyList<MEDIA_ASSET> Items, int TotalCount)> GetPagedByUploaderAsync(
        Guid uploadedByUserId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.MediaAssets()
            .AsNoTracking()
            .Where(a => a.UPLOADED_BY_USER_ID == uploadedByUserId)
            .OrderByDescending(a => a.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public void Add(MEDIA_ASSET mediaAsset) => dbContext.MediaAssets().Add(mediaAsset);

    /// <summary>Real hard delete — <see cref="MEDIA_ASSET"/> is not <see cref="Siri.Persistence.Conventions.ISoftDelete"/>,
    /// so <c>AuditableEntityInterceptor</c> does not intercept this into a flag update (contrast with
    /// <c>Course</c>) — see that entity's own doc comment for why a real delete is acceptable here.</summary>
    public void Remove(MEDIA_ASSET mediaAsset) => dbContext.MediaAssets().Remove(mediaAsset);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
