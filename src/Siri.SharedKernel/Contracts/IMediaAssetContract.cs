namespace Siri.SharedKernel.Contracts;

/// <summary>
/// Summary information about a media asset for cross-module consumption (e.g. Catalog, Learning).
/// </summary>
public sealed record MediaAssetSummary(
    Guid Id,
    Guid UploadedByUserId,
    string Status,
    int? DurationSeconds);

/// <summary>
/// Cross-module contract for querying media assets without direct project references between modules.
/// </summary>
public interface IMediaAssetContract
{
    Task<MediaAssetSummary?> GetAssetSummaryAsync(Guid mediaAssetId, CancellationToken cancellationToken);
}
