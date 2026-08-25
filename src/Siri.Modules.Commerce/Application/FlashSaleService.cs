using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// No <c>IFlashSaleService</c> interface — same reasoning as <c>OrderService</c>'s own doc comment.
/// <see cref="FLASH_SALE"/> is a public catalog-style resource (see its own doc comment) — same
/// public-read/admin-write split as <c>BundleService</c>'s own doc comment describes for
/// <see cref="BUNDLE"/>.
/// <para>
/// Primary constructor is safe here (contrast <c>RefundService</c>'s traditional constructor): the sole
/// dependency, <see cref="IFlashSaleRepository"/>, IS read below (by <see cref="GetByIdAsync"/>).
/// </para>
/// </summary>
public sealed class FlashSaleService(IFlashSaleRepository flashSaleRepository)
{
    public async Task<Result<FlashSaleResponse>> CreateAsync(CreateFlashSaleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            return Result.Failure<FlashSaleResponse>(DomainError.Validation("Title is required."));
        }

        if (command.EndsAtUtc <= command.StartsAtUtc)
        {
            return Result.Failure<FlashSaleResponse>(DomainError.Validation("EndsAtUtc must be after StartsAtUtc."));
        }

        if (command.Items is null || command.Items.Count == 0)
        {
            return Result.Failure<FlashSaleResponse>(DomainError.Validation("Flash sale must contain at least one item."));
        }

        foreach (var item in command.Items)
        {
            if (item.SalePrice < 0)
            {
                return Result.Failure<FlashSaleResponse>(DomainError.Validation("Sale price cannot be negative."));
            }
        }

        var flashSale = FLASH_SALE.Create(command.Title, command.StartsAtUtc, command.EndsAtUtc);

        foreach (var item in command.Items)
        {
            flashSale.AddItem(item.CourseId, item.SalePrice);
        }

        await flashSaleRepository.AddAsync(flashSale, cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(flashSale));
    }

    /// <summary>Public, no ownership to check — a plain fetch-by-id-or-404, same shape
    /// <c>BundleService.GetByIdAsync</c>'s own doc comment describes.</summary>
    public async Task<Result<FlashSaleResponse>> GetByIdAsync(Guid flashSaleId, CancellationToken cancellationToken)
    {
        var flashSale = await flashSaleRepository.GetByIdAsync(flashSaleId, cancellationToken).ConfigureAwait(false);
        if (flashSale is null) return Result.Failure<FlashSaleResponse>(DomainError.NotFound("ไม่พบแฟลชเซลที่ระบุ"));
        return ToResponse(flashSale);
    }

    /// <summary>Public storefront list — database.md: "รายการที่โตได้ต้อง paginate เสมอ".</summary>
    public async Task<PagedResult<FlashSaleResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

        var totalCount = await flashSaleRepository.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await flashSaleRepository.ListAsync(effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);

        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<FlashSaleResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    private static FlashSaleResponse ToResponse(FLASH_SALE flashSale) =>
        new(
            flashSale.FLASH_SALE_ID, flashSale.TITLE, flashSale.STARTS_AT_UTC, flashSale.ENDS_AT_UTC, flashSale.IS_ACTIVE,
            flashSale.FLASH_SALE_ITEMS.Select(i => new FlashSaleItemResponse(i.FLASH_SALE_ITEM_ID, i.COURSE_ID, i.SALE_PRICE)).ToList());
}

public sealed record FlashSaleResponse(
    Guid Id,
    string Title,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    bool IsActive,
    IReadOnlyList<FlashSaleItemResponse> Items);

public sealed record FlashSaleItemResponse(Guid Id, Guid CourseId, decimal SalePrice);

public sealed record CreateFlashSaleCommand(string Title, DateTime StartsAtUtc, DateTime EndsAtUtc, IReadOnlyList<CreateFlashSaleItemCommand> Items);

public sealed record CreateFlashSaleItemCommand(Guid CourseId, decimal SalePrice);
