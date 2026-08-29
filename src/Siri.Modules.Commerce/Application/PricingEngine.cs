using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

public interface IPricingEngine
{
    Task<Result<PricingCalculationResult>> CalculatePricingAsync(
        PricingCalculationRequest request,
        CancellationToken cancellationToken);
}

public sealed class PricingEngine(
    ICatalogPriceContract catalogPriceContract,
    IFlashSaleRepository flashSaleRepository,
    IBundleRepository bundleRepository,
    IPromoCodeRepository promoCodeRepository,
    IClock clock) : IPricingEngine
{
    public async Task<Result<PricingCalculationResult>> CalculatePricingAsync(
        PricingCalculationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CourseIds is null || request.CourseIds.Count == 0)
        {
            return Result.Failure<PricingCalculationResult>(DomainError.Validation("กรุณาระบุคอร์สเรียน"));
        }

        var distinctCourseIds = request.CourseIds.Distinct().ToList();
        var coursePrices = await catalogPriceContract.GetPublishedCoursePricesAsync(distinctCourseIds, cancellationToken).ConfigureAwait(false);
        if (coursePrices.Count != distinctCourseIds.Count)
        {
            return Result.Failure<PricingCalculationResult>(DomainError.NotFound("พบคอร์สเรียนบางรายการที่ไม่มีอยู่หรือยังไม่เปิดจำหน่าย"));
        }

        var now = clock.UtcNow;
        var originalSubtotal = distinctCourseIds.Sum(id => coursePrices[id].Price);

        // Step 1: Base prices
        var itemCalculations = distinctCourseIds.ToDictionary(
            id => id,
            id => new MutableItemPricing(
                CourseId: id,
                Title: coursePrices[id].Title,
                OriginalPrice: coursePrices[id].Price,
                PriceAfterFlashSale: coursePrices[id].Price,
                PriceAfterBundle: coursePrices[id].Price,
                FinalLineTotal: coursePrices[id].Price,
                DiscountSource: null));

        // Step 2: Precedence 1 - Flash Sale item price override
        var activeFlashSales = await flashSaleRepository.GetActiveFlashSalesAsync(now, cancellationToken).ConfigureAwait(false);
        var activeFlashSaleItems = activeFlashSales
            .SelectMany(f => f.FLASH_SALE_ITEMS)
            .GroupBy(i => i.COURSE_ID)
            .ToDictionary(g => g.Key, g => g.Min(i => i.SALE_PRICE));

        foreach (var courseId in distinctCourseIds)
        {
            if (activeFlashSaleItems.TryGetValue(courseId, out var flashSalePrice) && flashSalePrice < itemCalculations[courseId].OriginalPrice)
            {
                var item = itemCalculations[courseId];
                item.PriceAfterFlashSale = flashSalePrice;
                item.PriceAfterBundle = flashSalePrice;
                item.FinalLineTotal = flashSalePrice;
                item.DiscountSource = "FlashSale";
            }
        }

        var flashSaleSavings = distinctCourseIds.Sum(id => itemCalculations[id].OriginalPrice - itemCalculations[id].PriceAfterFlashSale);

        // Step 3: Precedence 2 - Bundle Subtotal
        BUNDLE? appliedBundle = null;
        decimal bundleSavings = 0m;

        if (request.BundleId.HasValue)
        {
            var bundle = await bundleRepository.GetByIdAsync(request.BundleId.Value, cancellationToken).ConfigureAwait(false);
            if (bundle is not null && bundle.IS_ACTIVE)
            {
                var bundleCourseIds = bundle.BUNDLE_ITEMS.Select(b => b.COURSE_ID).ToHashSet();
                if (bundleCourseIds.IsSubsetOf(distinctCourseIds))
                {
                    var sumBeforeBundle = bundleCourseIds.Sum(id => itemCalculations[id].PriceAfterFlashSale);
                    if (bundle.PRICE < sumBeforeBundle)
                    {
                        appliedBundle = bundle;
                        bundleSavings = sumBeforeBundle - bundle.PRICE;

                        // Allocate bundle price proportionally among bundle items
                        foreach (var courseId in bundleCourseIds)
                        {
                            var item = itemCalculations[courseId];
                            var proportion = sumBeforeBundle > 0 ? item.PriceAfterFlashSale / sumBeforeBundle : 1m / bundleCourseIds.Count;
                            item.PriceAfterBundle = Math.Round(bundle.PRICE * proportion, 2);
                            item.FinalLineTotal = item.PriceAfterBundle;
                            item.DiscountSource = item.DiscountSource == "FlashSale" ? "FlashSale+Bundle" : "Bundle";
                        }
                    }
                }
            }
        }

        var subtotalAfterItemDiscounts = distinctCourseIds.Sum(id => itemCalculations[id].PriceAfterBundle);

        // Step 4: Precedence 3 - Promo Code Application
        PROMO_CODE? appliedPromo = null;
        var promoDiscount = 0m;

        if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            var normalizedCode = request.PromoCode.Trim().ToUpperInvariant();
            var promo = await promoCodeRepository.GetByCodeAsync(normalizedCode, cancellationToken).ConfigureAwait(false);
            if (promo is null || !promo.IS_ACTIVE)
            {
                return Result.Failure<PricingCalculationResult>(DomainError.Validation("โค้ดส่วนลดไม่ถูกต้องหรือถูกปิดใช้งาน"));
            }

            if (now < promo.STARTS_AT_UTC || now > promo.ENDS_AT_UTC)
            {
                return Result.Failure<PricingCalculationResult>(DomainError.Validation("โค้ดส่วนลดหมดอายุหรือไม่สามารถใช้งานได้ในขณะนี้"));
            }

            if (promo.REDEEMED_COUNT >= promo.MAX_REDEMPTIONS)
            {
                return Result.Failure<PricingCalculationResult>(DomainError.Conflict("โค้ดส่วนลดถูกใช้งานจนครบโควตาแล้ว"));
            }

            var userRedemptions = await promoCodeRepository.GetUserRedemptionCountAsync(promo.PROMO_CODE_ID, request.UserId, cancellationToken).ConfigureAwait(false);
            if (userRedemptions >= promo.MAX_PER_USER)
            {
                return Result.Failure<PricingCalculationResult>(DomainError.Validation("คุณได้ใช้โค้ดส่วนลดนี้ครบตามจำนวนสิทธิ์ที่กำหนดแล้ว"));
            }

            decimal applicableSubtotal;
            switch (promo.SCOPE)
            {
                case PromoCodeScope.AllCourses:
                    applicableSubtotal = subtotalAfterItemDiscounts;
                    break;
                case PromoCodeScope.Course:
                    applicableSubtotal = promo.SCOPE_REF_ID.HasValue && distinctCourseIds.Contains(promo.SCOPE_REF_ID.Value)
                        ? itemCalculations[promo.SCOPE_REF_ID.Value].PriceAfterBundle
                        : 0m;
                    if (applicableSubtotal == 0m)
                    {
                        return Result.Failure<PricingCalculationResult>(DomainError.Validation("โค้ดส่วนลดนี้ใช้ได้เฉพาะคอร์สที่กำหนด"));
                    }
                    break;
                default:
                    applicableSubtotal = subtotalAfterItemDiscounts;
                    break;
            }

            if (applicableSubtotal < promo.MIN_ORDER_AMOUNT)
            {
                return Result.Failure<PricingCalculationResult>(DomainError.Validation($"ยอดสั่งซื้อขั้นต่ำสำหรับโค้ดนี้คือ ฿{promo.MIN_ORDER_AMOUNT:N0}"));
            }

            promoDiscount = promo.DISCOUNT_TYPE switch
            {
                PromoCodeDiscountType.Fixed => Math.Min(applicableSubtotal, promo.DISCOUNT_VALUE),
                PromoCodeDiscountType.Percentage => Math.Round(applicableSubtotal * (promo.DISCOUNT_VALUE / 100m), 2),
                _ => 0m,
            };

            appliedPromo = promo;

            // Distribute promo discount across items
            if (promo.SCOPE == PromoCodeScope.Course && promo.SCOPE_REF_ID.HasValue)
            {
                var targetItem = itemCalculations[promo.SCOPE_REF_ID.Value];
                targetItem.FinalLineTotal = Math.Max(0m, targetItem.PriceAfterBundle - promoDiscount);
                targetItem.DiscountSource = targetItem.DiscountSource is not null
                    ? $"{targetItem.DiscountSource}+Promo"
                    : "Promo";
            }
            else if (subtotalAfterItemDiscounts > 0 && promoDiscount > 0)
            {
                foreach (var courseId in distinctCourseIds)
                {
                    var item = itemCalculations[courseId];
                    var proportion = item.PriceAfterBundle / subtotalAfterItemDiscounts;
                    var itemDiscount = Math.Round(promoDiscount * proportion, 2);
                    item.FinalLineTotal = Math.Max(0m, item.PriceAfterBundle - itemDiscount);
                    item.DiscountSource = item.DiscountSource is not null
                        ? $"{item.DiscountSource}+Promo"
                        : "Promo";
                }
            }
        }

        // Step 5: Precedence 4 - Total & VAT 7% inclusive extraction
        var totalAmount = Math.Max(0m, subtotalAfterItemDiscounts - promoDiscount);
        var taxAmount = Math.Round(totalAmount * 7m / 107m, 2);

        var breakdowns = distinctCourseIds.Select(id =>
        {
            var item = itemCalculations[id];
            return new PricingItemBreakdown(
                item.CourseId,
                item.Title,
                item.OriginalPrice,
                item.PriceAfterBundle,
                item.FinalLineTotal,
                item.DiscountSource);
        }).ToList();

        var calculationResult = new PricingCalculationResult(
            OriginalSubtotal: originalSubtotal,
            FlashSaleSavings: flashSaleSavings,
            BundleSavings: bundleSavings,
            PromoDiscount: promoDiscount,
            SubtotalAfterItemDiscounts: subtotalAfterItemDiscounts,
            TotalAmount: totalAmount,
            TaxAmount: taxAmount,
            AppliedPromoCode: appliedPromo,
            AppliedBundle: appliedBundle,
            Items: breakdowns);

        return Result.Success(calculationResult);
    }

    private sealed class MutableItemPricing(
        Guid CourseId,
        string Title,
        decimal OriginalPrice,
        decimal PriceAfterFlashSale,
        decimal PriceAfterBundle,
        decimal FinalLineTotal,
        string? DiscountSource)
    {
        public Guid CourseId { get; } = CourseId;
        public string Title { get; } = Title;
        public decimal OriginalPrice { get; } = OriginalPrice;
        public decimal PriceAfterFlashSale { get; set; } = PriceAfterFlashSale;
        public decimal PriceAfterBundle { get; set; } = PriceAfterBundle;
        public decimal FinalLineTotal { get; set; } = FinalLineTotal;
        public string? DiscountSource { get; set; } = DiscountSource;
    }
}

public sealed record PricingCalculationRequest(
    Guid UserId,
    IReadOnlyList<Guid> CourseIds,
    Guid? BundleId = null,
    string? PromoCode = null);

public sealed record PricingItemBreakdown(
    Guid CourseId,
    string Title,
    decimal OriginalBaselinePrice,
    decimal EffectiveItemPrice,
    decimal FinalLineTotal,
    string? DiscountSource);

public sealed record PricingCalculationResult(
    decimal OriginalSubtotal,
    decimal FlashSaleSavings,
    decimal BundleSavings,
    decimal PromoDiscount,
    decimal SubtotalAfterItemDiscounts,
    decimal TotalAmount,
    decimal TaxAmount,
    PROMO_CODE? AppliedPromoCode,
    BUNDLE? AppliedBundle,
    IReadOnlyList<PricingItemBreakdown> Items);
