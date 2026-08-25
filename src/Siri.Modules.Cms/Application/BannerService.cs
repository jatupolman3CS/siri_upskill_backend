using Siri.Modules.Cms.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

public sealed class BannerService(IBannerRepository repository)
{
    public async Task<Result<BannerResponse>> CreateAsync(CreateBannerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await repository.GetByPlacementAsync(command.Placement, cancellationToken).ConfigureAwait(false);
        var sortOrder = existing.Count == 0 ? 0 : existing.Max(b => b.SORT_ORDER) + 1;

        var banner = BANNER.Create(
            command.Placement,
            command.ImageUrl,
            command.Title,
            sortOrder,
            command.MobileImageUrl,
            command.LinkUrl,
            command.StartsAtUtc,
            command.EndsAtUtc);

        repository.Add(banner);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(banner));
    }

    public async Task<Result<BannerResponse>> UpdateAsync(Guid id, UpdateBannerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var banner = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (banner is null)
        {
            return Result.Failure<BannerResponse>(DomainError.NotFound("ไม่พบแบนเนอร์"));
        }

        banner.Update(
            command.ImageUrl,
            command.MobileImageUrl,
            command.LinkUrl,
            command.Title,
            command.StartsAtUtc,
            command.EndsAtUtc,
            command.IsActive);

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(banner));
    }

    public async Task<Result> ReorderAsync(ReorderBannersCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Items.Count == 0)
        {
            return Result.Success();
        }

        var firstId = command.Items[0].BannerId;
        var firstBanner = await repository.GetByIdAsync(firstId, cancellationToken).ConfigureAwait(false);
        if (firstBanner is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบแบนเนอร์"));
        }

        var banners = await repository.GetByPlacementAsync(firstBanner.PLACEMENT, cancellationToken).ConfigureAwait(false);
        var bannerMap = banners.ToDictionary(b => b.BANNER_ID);

        if (command.Items.Count != banners.Count || !command.Items.All(i => bannerMap.ContainsKey(i.BannerId)))
        {
            return Result.Failure(DomainError.Validation("ต้องระบุ ID แบนเนอร์ให้ครบทุกรายการในตำแหน่งนี้"));
        }

        foreach (var item in command.Items)
        {
            bannerMap[item.BannerId].SetSortOrder(item.SortOrder);
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var banner = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (banner is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบแบนเนอร์"));
        }

        repository.Remove(banner);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<PagedResult<BannerResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var paged = await repository.GetPagedAsync(effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var mapped = paged.Items.Select(ToResponse).ToList();
        return PagedResult<BannerResponse>.Create(mapped, paged.TotalCount, effectivePage, effectivePageSize);
    }

    private static BannerResponse ToResponse(BANNER b) =>
        new(
            b.BANNER_ID,
            b.PLACEMENT,
            b.IMAGE_URL,
            b.MOBILE_IMAGE_URL,
            b.LINK_URL,
            b.TITLE,
            b.SORT_ORDER,
            b.STARTS_AT_UTC,
            b.ENDS_AT_UTC,
            b.IS_ACTIVE);
}
