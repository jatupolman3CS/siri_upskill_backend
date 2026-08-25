using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

public sealed class PromoCodeService(
    IPromoCodeRepository promoCodeRepository,
    ICatalogPriceContract catalogPriceContract,
    IClock clock)
{
    public async Task<Result<PromoCodeResponse>> CreateAsync(CreatePromoCodeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedCode = command.Code.Trim().ToUpperInvariant();
        var existing = await promoCodeRepository.GetByCodeAsync(normalizedCode, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Failure<PromoCodeResponse>(DomainError.Conflict("มีโค้ดส่วนลดนี้อยู่ในระบบแล้ว"));
        }

        var promoCode = PROMO_CODE.Create(
            normalizedCode,
            command.DiscountType,
            command.DiscountValue,
            command.MaxRedemptions,
            command.MaxPerUser,
            command.MinOrderAmount,
            command.StartsAtUtc,
            command.EndsAtUtc,
            command.Scope,
            command.ScopeRefId);

        await promoCodeRepository.AddAsync(promoCode, cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(promoCode));
    }

    public async Task<Result<PromoCodeResponse>> GetByIdAsync(Guid promoCodeId, CancellationToken cancellationToken)
    {
        var promoCode = await promoCodeRepository.GetByIdAsync(promoCodeId, cancellationToken).ConfigureAwait(false);
        if (promoCode is null) return Result.Failure<PromoCodeResponse>(DomainError.NotFound("ไม่พบโค้ดส่วนลดที่ระบุ"));
        return ToResponse(promoCode);
    }

    public async Task<PagedResult<PromoCodeResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var totalCount = await promoCodeRepository.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await promoCodeRepository.ListAsync(effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);

        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<PromoCodeResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    public async Task<Result<ValidatePromoCodeResponse>> ValidatePromoCodeAsync(
        Guid userId,
        ValidatePromoCodeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Code))
        {
            return Result.Failure<ValidatePromoCodeResponse>(DomainError.Validation("กรุณาระบุโค้ดส่วนลด"));
        }

        if (command.CourseIds is null || command.CourseIds.Count == 0)
        {
            return Result.Failure<ValidatePromoCodeResponse>(DomainError.Validation("กรุณาระบุคอร์สเรียนในตะกร้า"));
        }

        var normalizedCode = command.Code.Trim().ToUpperInvariant();
        var promo = await promoCodeRepository.GetByCodeAsync(normalizedCode, cancellationToken).ConfigureAwait(false);
        if (promo is null)
        {
            return Result.Success(new ValidatePromoCodeResponse(false, normalizedCode, null, null, 0m, 0m, 0m, "ไม่พบโค้ดส่วนลดนี้"));
        }

        if (!promo.IS_ACTIVE)
        {
            return Result.Success(new ValidatePromoCodeResponse(false, normalizedCode, promo.DISCOUNT_TYPE, promo.DISCOUNT_VALUE, 0m, 0m, 0m, "โค้ดส่วนลดนี้ถูกปิดใช้งานแล้ว"));
        }

        var now = clock.UtcNow;
        if (now < promo.STARTS_AT_UTC)
        {
            return Result.Success(new ValidatePromoCodeResponse(false, normalizedCode, promo.DISCOUNT_TYPE, promo.DISCOUNT_VALUE, 0m, 0m, 0m, "โค้ดส่วนลดนี้ยังไม่เริ่มใช้งาน"));
        }

        if (now > promo.ENDS_AT_UTC)
        {
            return Result.Success(new ValidatePromoCodeResponse(false, normalizedCode, promo.DISCOUNT_TYPE, promo.DISCOUNT_VALUE, 0m, 0m, 0m, "โค้ดส่วนลดนี้หมดอายุแล้ว"));
        }

        if (promo.REDEEMED_COUNT >= promo.MAX_REDEMPTIONS)
        {
            return Result.Success(new ValidatePromoCodeResponse(false, normalizedCode, promo.DISCOUNT_TYPE, promo.DISCOUNT_VALUE, 0m, 0m, 0m, "โค้ดส่วนลดนี้ถูกใช้งานจนครบโควตาแล้ว"));
        }

        var userRedemptions = await promoCodeRepository.GetUserRedemptionCountAsync(promo.PROMO_CODE_ID, userId, cancellationToken).ConfigureAwait(false);
        if (userRedemptions >= promo.MAX_PER_USER)
        {
            return Result.Success(new ValidatePromoCodeResponse(false, normalizedCode, promo.DISCOUNT_TYPE, promo.DISCOUNT_VALUE, 0m, 0m, 0m, "คุณได้ใช้โค้ดส่วนลดนี้ครบตามจำนวนสิทธิ์ที่กำหนดแล้ว"));
        }

        var coursePrices = await catalogPriceContract.GetPublishedCoursePricesAsync(command.CourseIds, cancellationToken).ConfigureAwait(false);
        if (coursePrices.Count != command.CourseIds.Distinct().Count())
        {
            return Result.Failure<ValidatePromoCodeResponse>(DomainError.NotFound("พบคอร์สเรียนบางรายการที่ไม่มีอยู่หรือยังไม่เปิดจำหน่าย"));
        }

        var subtotal = coursePrices.Values.Sum(c => c.Price);

        decimal applicableSubtotal;
        switch (promo.SCOPE)
        {
            case PromoCodeScope.AllCourses:
                applicableSubtotal = subtotal;
                break;
            case PromoCodeScope.Course:
                applicableSubtotal = promo.SCOPE_REF_ID.HasValue && command.CourseIds.Contains(promo.SCOPE_REF_ID.Value) && coursePrices.TryGetValue(promo.SCOPE_REF_ID.Value, out var coursePrice)
                    ? coursePrice.Price
                    : 0m;
                if (applicableSubtotal == 0m)
                {
                    return Result.Success(new ValidatePromoCodeResponse(false, normalizedCode, promo.DISCOUNT_TYPE, promo.DISCOUNT_VALUE, subtotal, 0m, subtotal, "โค้ดส่วนลดนี้ใช้ได้เฉพาะคอร์สที่กำหนด"));
                }
                break;
            default:
                applicableSubtotal = subtotal;
                break;
        }

        if (applicableSubtotal < promo.MIN_ORDER_AMOUNT)
        {
            return Result.Success(new ValidatePromoCodeResponse(false, normalizedCode, promo.DISCOUNT_TYPE, promo.DISCOUNT_VALUE, subtotal, 0m, subtotal, $"ยอดสั่งซื้อขั้นต่ำสำหรับโค้ดนี้คือ ฿{promo.MIN_ORDER_AMOUNT:N0}"));
        }

        decimal discountAmount = promo.DISCOUNT_TYPE switch
        {
            PromoCodeDiscountType.Fixed => Math.Min(applicableSubtotal, promo.DISCOUNT_VALUE),
            PromoCodeDiscountType.Percentage => Math.Round(applicableSubtotal * (promo.DISCOUNT_VALUE / 100m), 2),
            _ => 0m,
        };

        var totalAmount = Math.Max(0m, subtotal - discountAmount);

        return Result.Success(new ValidatePromoCodeResponse(
            true,
            normalizedCode,
            promo.DISCOUNT_TYPE,
            promo.DISCOUNT_VALUE,
            subtotal,
            discountAmount,
            totalAmount,
            null));
    }

    private static PromoCodeResponse ToResponse(PROMO_CODE promoCode) =>
        new(
            promoCode.PROMO_CODE_ID, promoCode.CODE, promoCode.DISCOUNT_TYPE, promoCode.DISCOUNT_VALUE,
            promoCode.MAX_REDEMPTIONS, promoCode.REDEEMED_COUNT, promoCode.MAX_PER_USER, promoCode.MIN_ORDER_AMOUNT,
            promoCode.STARTS_AT_UTC, promoCode.ENDS_AT_UTC, promoCode.SCOPE, promoCode.SCOPE_REF_ID, promoCode.IS_ACTIVE);
}

public sealed record PromoCodeResponse(
    Guid Id,
    string Code,
    PromoCodeDiscountType DiscountType,
    decimal DiscountValue,
    int MaxRedemptions,
    int RedeemedCount,
    int MaxPerUser,
    decimal MinOrderAmount,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    PromoCodeScope Scope,
    Guid? ScopeRefId,
    bool IsActive);

public sealed record CreatePromoCodeCommand(
    string Code,
    PromoCodeDiscountType DiscountType,
    decimal DiscountValue,
    int MaxRedemptions,
    int MaxPerUser,
    decimal MinOrderAmount,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    PromoCodeScope Scope,
    Guid? ScopeRefId);

public sealed record ValidatePromoCodeCommand(
    string Code,
    IReadOnlyList<Guid> CourseIds);

public sealed record ValidatePromoCodeResponse(
    bool IsValid,
    string Code,
    PromoCodeDiscountType? DiscountType,
    decimal? DiscountValue,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TotalAmount,
    string? ErrorMessage);
