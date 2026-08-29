using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class PricingEngineTests
{
    private readonly FakeCatalogPriceContract _catalogPriceContract = new();
    private readonly FakeFlashSaleRepository _flashSaleRepo = new();
    private readonly FakeBundleRepository _bundleRepo = new();
    private readonly FakePromoCodeRepository _promoCodeRepo = new();
    private readonly FakeClock _clock = new(new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc));

    private PricingEngine CreateEngine() =>
        new(_catalogPriceContract, _flashSaleRepo, _bundleRepo, _promoCodeRepo, _clock);

    [Fact]
    public async Task CalculatePricing_BaselinePriceOnly_ReturnsOriginalPricesAnd7PercentVat()
    {
        var engine = CreateEngine();
        var courseId = Guid.NewGuid();
        _catalogPriceContract.Prices[courseId] = new CoursePriceInfo(courseId, "C# Mastery", 1070m, Guid.NewGuid(), 30);

        var request = new PricingCalculationRequest(Guid.NewGuid(), [courseId]);
        var result = await engine.CalculatePricingAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var pricing = result.Value;
        Assert.Equal(1070m, pricing.OriginalSubtotal);
        Assert.Equal(0m, pricing.FlashSaleSavings);
        Assert.Equal(0m, pricing.BundleSavings);
        Assert.Equal(0m, pricing.PromoDiscount);
        Assert.Equal(1070m, pricing.TotalAmount);
        Assert.Equal(70m, pricing.TaxAmount); // 1070 * 7 / 107 = 70
        Assert.Single(pricing.Items);
        Assert.Equal(1070m, pricing.Items[0].FinalLineTotal);
        Assert.Null(pricing.Items[0].DiscountSource);
    }

    [Fact]
    public async Task CalculatePricing_Precedence1_FlashSaleOverridesBaseline()
    {
        var engine = CreateEngine();
        var courseId = Guid.NewGuid();
        _catalogPriceContract.Prices[courseId] = new CoursePriceInfo(courseId, "Docker Deep Dive", 2000m, Guid.NewGuid(), 30);

        var flashSale = FLASH_SALE.Create("8.8 Mega Flash Sale", _clock.UtcNow.AddHours(-1), _clock.UtcNow.AddHours(2));
        flashSale.AddItem(courseId, 1200m);
        await _flashSaleRepo.AddAsync(flashSale, CancellationToken.None);

        var request = new PricingCalculationRequest(Guid.NewGuid(), [courseId]);
        var result = await engine.CalculatePricingAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var pricing = result.Value;
        Assert.Equal(2000m, pricing.OriginalSubtotal);
        Assert.Equal(800m, pricing.FlashSaleSavings);
        Assert.Equal(0m, pricing.BundleSavings);
        Assert.Equal(1200m, pricing.SubtotalAfterItemDiscounts);
        Assert.Equal(1200m, pricing.TotalAmount);
        Assert.Equal(Math.Round(1200m * 7m / 107m, 2), pricing.TaxAmount);
        Assert.Equal("FlashSale", pricing.Items[0].DiscountSource);
        Assert.Equal(1200m, pricing.Items[0].FinalLineTotal);
    }

    [Fact]
    public async Task CalculatePricing_Precedence2_BundleOverridesFlashSalePrices()
    {
        var engine = CreateEngine();
        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();
        _catalogPriceContract.Prices[course1] = new CoursePriceInfo(course1, "COURSE 1", 1000m, Guid.NewGuid(), 30);
        _catalogPriceContract.Prices[course2] = new CoursePriceInfo(course2, "COURSE 2", 1000m, Guid.NewGuid(), 30);

        // Bundle is 1500 THB for both courses (saving 500)
        var bundle = BUNDLE.Create("dev-pack", "Developer Pack", "All in one", 1500m);
        bundle.AddItem(course1);
        bundle.AddItem(course2);
        await _bundleRepo.AddAsync(bundle, CancellationToken.None);

        var request = new PricingCalculationRequest(Guid.NewGuid(), [course1, course2], BundleId: bundle.BUNDLE_ID);
        var result = await engine.CalculatePricingAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var pricing = result.Value;
        Assert.Equal(2000m, pricing.OriginalSubtotal);
        Assert.Equal(500m, pricing.BundleSavings);
        Assert.Equal(1500m, pricing.SubtotalAfterItemDiscounts);
        Assert.Equal(1500m, pricing.TotalAmount);
        Assert.Equal(bundle.BUNDLE_ID, pricing.AppliedBundle?.BUNDLE_ID);
    }

    [Fact]
    public async Task CalculatePricing_Precedence3_PromoCodeApplied_AfterFlashAndBundle()
    {
        var engine = CreateEngine();
        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();
        _catalogPriceContract.Prices[course1] = new CoursePriceInfo(course1, "COURSE 1", 1000m, Guid.NewGuid(), 30);
        _catalogPriceContract.Prices[course2] = new CoursePriceInfo(course2, "COURSE 2", 1000m, Guid.NewGuid(), 30);

        // Bundle = 1600 (saves 400)
        var bundle = BUNDLE.Create("dev-pack", "Developer Pack", "All in one", 1600m);
        bundle.AddItem(course1);
        bundle.AddItem(course2);
        await _bundleRepo.AddAsync(bundle, CancellationToken.None);

        // Promo code: 10% off remaining subtotal
        var promo = PROMO_CODE.Create("SAVE10", PromoCodeDiscountType.Percentage, 10m, 1000, 1, 500m, _clock.UtcNow.AddDays(-1), _clock.UtcNow.AddDays(1), PromoCodeScope.AllCourses, null);
        await _promoCodeRepo.AddAsync(promo, CancellationToken.None);

        var request = new PricingCalculationRequest(Guid.NewGuid(), [course1, course2], BundleId: bundle.BUNDLE_ID, PromoCode: "SAVE10");
        var result = await engine.CalculatePricingAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var pricing = result.Value;
        Assert.Equal(2000m, pricing.OriginalSubtotal);
        Assert.Equal(400m, pricing.BundleSavings);
        Assert.Equal(1600m, pricing.SubtotalAfterItemDiscounts);
        Assert.Equal(160m, pricing.PromoDiscount); // 10% of 1600
        Assert.Equal(1440m, pricing.TotalAmount);  // 1600 - 160 = 1440
        Assert.Equal(Math.Round(1440m * 7m / 107m, 2), pricing.TaxAmount);
    }

    [Fact]
    public async Task CalculatePricing_CourseScopedPromoCode_AppliesOnlyToTargetCourse()
    {
        var engine = CreateEngine();
        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();
        _catalogPriceContract.Prices[course1] = new CoursePriceInfo(course1, "COURSE 1", 1000m, Guid.NewGuid(), 30);
        _catalogPriceContract.Prices[course2] = new CoursePriceInfo(course2, "COURSE 2", 2000m, Guid.NewGuid(), 30);

        // Promo code for COURSE 1 only: Fixed 200 off
        var promo = PROMO_CODE.Create("C1SPECIAL", PromoCodeDiscountType.Fixed, 200m, 100, 1, 500m, _clock.UtcNow.AddDays(-1), _clock.UtcNow.AddDays(1), PromoCodeScope.Course, course1);
        await _promoCodeRepo.AddAsync(promo, CancellationToken.None);

        var request = new PricingCalculationRequest(Guid.NewGuid(), [course1, course2], PromoCode: "C1SPECIAL");
        var result = await engine.CalculatePricingAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var pricing = result.Value;
        Assert.Equal(3000m, pricing.OriginalSubtotal);
        Assert.Equal(200m, pricing.PromoDiscount);
        Assert.Equal(2800m, pricing.TotalAmount);

        var item1 = pricing.Items.First(i => i.CourseId == course1);
        var item2 = pricing.Items.First(i => i.CourseId == course2);
        Assert.Equal(800m, item1.FinalLineTotal);
        Assert.Equal(2000m, item2.FinalLineTotal);
    }

    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        public readonly Dictionary<Guid, CoursePriceInfo> Prices = [];

        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(courseIds.Where(Prices.ContainsKey).ToDictionary(id => id, id => Prices[id]));

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }

    private sealed class FakeFlashSaleRepository : IFlashSaleRepository
    {
        public readonly Dictionary<Guid, FLASH_SALE> FlashSales = [];

        public Task<FLASH_SALE?> GetByIdAsync(Guid flashSaleId, CancellationToken cancellationToken) =>
            Task.FromResult(FlashSales.TryGetValue(flashSaleId, out var f) ? f : null);

        public Task AddAsync(FLASH_SALE flashSale, CancellationToken cancellationToken)
        {
            FlashSales[flashSale.FLASH_SALE_ID] = flashSale;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FLASH_SALE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FLASH_SALE>>(FlashSales.Values.ToList());

        public Task<IReadOnlyList<FLASH_SALE>> GetActiveFlashSalesAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FLASH_SALE>>(FlashSales.Values.Where(f => f.IS_ACTIVE && f.STARTS_AT_UTC <= nowUtc && f.ENDS_AT_UTC >= nowUtc).ToList());

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(FlashSales.Count);
    }

    private sealed class FakeBundleRepository : IBundleRepository
    {
        public readonly Dictionary<Guid, BUNDLE> Bundles = [];

        public Task<BUNDLE?> GetByIdAsync(Guid bundleId, CancellationToken cancellationToken) =>
            Task.FromResult(Bundles.TryGetValue(bundleId, out var b) ? b : null);

        public Task AddAsync(BUNDLE bundle, CancellationToken cancellationToken)
        {
            Bundles[bundle.BUNDLE_ID] = bundle;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BUNDLE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BUNDLE>>(Bundles.Values.ToList());

        public Task<IReadOnlyList<BUNDLE>> GetActiveBundlesAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BUNDLE>>(Bundles.Values.Where(b => b.IS_ACTIVE && (!b.STARTS_AT_UTC.HasValue || b.STARTS_AT_UTC <= nowUtc) && (!b.ENDS_AT_UTC.HasValue || b.ENDS_AT_UTC >= nowUtc)).ToList());

        public Task<IReadOnlyList<BUNDLE>> GetBundlesByCourseIdAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BUNDLE>>(Bundles.Values.Where(b => b.IS_ACTIVE && b.BUNDLE_ITEMS.Any(i => i.COURSE_ID == courseId)).ToList());

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Bundles.Count);
    }

    private sealed class FakePromoCodeRepository : IPromoCodeRepository
    {
        public readonly Dictionary<Guid, PROMO_CODE> Codes = [];

        public Task<PROMO_CODE?> GetByIdAsync(Guid promoCodeId, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.TryGetValue(promoCodeId, out var c) ? c : null);

        public Task<PROMO_CODE?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Values.FirstOrDefault(c => c.CODE.Equals(code, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(PROMO_CODE promoCode, CancellationToken cancellationToken)
        {
            Codes[promoCode.PROMO_CODE_ID] = promoCode;
            return Task.CompletedTask;
        }

        public Task<bool> TryRedeemAsync(Guid promoCodeId, Guid orderId, Guid userId, int maxPerUser, IClock clock, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<int> GetUserRedemptionCountAsync(Guid promoCodeId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task RevertRedemptionAsync(Guid promoCodeId, Guid orderId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PROMO_CODE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PROMO_CODE>>(Codes.Values.ToList());

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Count);
    }
}
