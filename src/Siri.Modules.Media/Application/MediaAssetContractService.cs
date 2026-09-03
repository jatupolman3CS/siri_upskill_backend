using Microsoft.EntityFrameworkCore;
using Siri.Modules.Media.Domain;
using Siri.Modules.Media.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Media.Application;

public sealed class MediaAssetContractService : IMediaAssetContract
{
    private readonly AppDbContext _dbContext;

    public MediaAssetContractService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<MediaAssetSummary?> GetAssetSummaryAsync(Guid mediaAssetId, CancellationToken cancellationToken)
    {
        var asset = await _dbContext.MediaAssets()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.MEDIA_ASSET_ID == mediaAssetId, cancellationToken);

        if (asset is null)
        {
            return null;
        }

        return new MediaAssetSummary(
            asset.MEDIA_ASSET_ID,
            asset.UPLOADED_BY_USER_ID,
            asset.STATUS.ToString(),
            asset.DURATION_SECONDS);
    }
}
