using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class PromoCodeServiceTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; set; } = now;
    }

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        public readonly Dictionary<Guid, CoursePriceInfo> Prices = [];

        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken)
        {
            var result = courseIds
                .Where(id => Prices.ContainsKey(id))
                .ToDictionary(id => id, id => Prices[id]);
            return Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(result);
        }

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class FakePromoCodeRepository : IPromoCodeRepository
    {
        public readonly Dictionary<Guid, PROMO_CODE> Codes = [];
        public readonly List<PROMO_REDEMPTION> Redemptions = [];

        public Task<PROMO_CODE?> GetByIdAsync(Guid promoCodeId, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.TryGetValue(promoCodeId, out var code) ? code : null);

        public Task<PROMO_CODE?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Values.FirstOrDefault(p => p.CODE.Equals(code, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(PROMO_CODE promoCode, CancellationToken cancellationToken)
        {
            Codes[promoCode.PROMO_CODE_ID] = promoCode;
            return Task.CompletedTask;
        }

        public Task<bool> TryRedeemAsync(Guid promoCodeId, Guid orderId, Guid userId, int maxPerUser, IClock clock, CancellationToken cancellationToken)
        {
            if (!Codes.TryGetValue(promoCodeId, out var code)) return Task.FromResult(false);
            if (!code.IS_ACTIVE || code.REDEEMED_COUNT >= code.MAX_REDEMPTIONS) return Task.FromResult(false);
            var userCount = Redemptions.Count(r => r.PROMO_CODE_ID == promoCodeId && r.USER_ID == userId);
            if (userCount >= maxPerUser) return Task.FromResult(false);

            Redemptions.Add(PROMO_REDEMPTION.Create(promoCodeId, orderId, userId, clock));
            return Task.FromResult(true);
        }

        public Task<int> GetUserRedemptionCountAsync(Guid promoCodeId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Redemptions.Count(r => r.PROMO_CODE_ID == promoCodeId && r.USER_ID == userId));

        public Task RevertRedemptionAsync(Guid promoCodeId, Guid orderId, CancellationToken cancellationToken)
        {
            Redemptions.RemoveAll(r => r.PROMO_CODE_ID == promoCodeId && r.ORDER_ID == orderId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PROMO_CODE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PROMO_CODE>>(Codes.Values.Skip((page - 1) * pageSize).Take(pageSize).ToList());

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Count);
    }

    [Fact]
    public async Task CreateAsync_CreatesPromoCodeWithNormalizedCode()
    {
        var repo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new PromoCodeService(repo, catalog, clock);

        var now = DateTime.UtcNow;
        var command = new CreatePromoCodeCommand(
            " siri2026 ",
            PromoCodeDiscountType.Fixed,
            100m,
            50,
            1,
            500m,
            now,
            now.AddDays(30),
            PromoCodeScope.AllCourses,
            null);

        var result = await service.CreateAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("SIRI2026", result.Value.Code);
        Assert.Equal(PromoCodeDiscountType.Fixed, result.Value.DiscountType);
        Assert.Equal(100m, result.Value.DiscountValue);
        Assert.Equal(50, result.Value.MaxRedemptions);
        Assert.Single(repo.Codes);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ReturnsConflict()
    {
        var repo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new PromoCodeService(repo, catalog, clock);

        var now = DateTime.UtcNow;
        var command = new CreatePromoCodeCommand(
            "DISCOUNT10",
            PromoCodeDiscountType.Percentage,
            10m,
            100,
            1,
            0m,
            now,
            now.AddDays(10),
            PromoCodeScope.AllCourses,
            null);

        var first = await service.CreateAsync(command, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var duplicate = await service.CreateAsync(command, CancellationToken.None);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("conflict", duplicate.Error.Code);
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_ValidFixedDiscount_ReturnsDiscount()
    {
        var repo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var service = new PromoCodeService(repo, catalog, clock);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "Course 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("SAVE200", PromoCodeDiscountType.Fixed, 200m, 10, 1, 500m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await repo.AddAsync(promo, CancellationToken.None);

        var result = await service.ValidatePromoCodeAsync(Guid.NewGuid(), new ValidatePromoCodeCommand("save200", [courseId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsValid);
        Assert.Equal("SAVE200", result.Value.Code);
        Assert.Equal(1000m, result.Value.Subtotal);
        Assert.Equal(200m, result.Value.DiscountAmount);
        Assert.Equal(800m, result.Value.TotalAmount);
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_ValidPercentageDiscount_ReturnsCalculatedDiscount()
    {
        var repo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var service = new PromoCodeService(repo, catalog, clock);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "Course 1", 1500m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("PERCENT20", PromoCodeDiscountType.Percentage, 20m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await repo.AddAsync(promo, CancellationToken.None);

        var result = await service.ValidatePromoCodeAsync(Guid.NewGuid(), new ValidatePromoCodeCommand("percent20", [courseId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsValid);
        Assert.Equal(1500m, result.Value.Subtotal);
        Assert.Equal(300m, result.Value.DiscountAmount);
        Assert.Equal(1200m, result.Value.TotalAmount);
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_ExpiredCode_ReturnsInvalid()
    {
        var repo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var service = new PromoCodeService(repo, catalog, clock);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "Course 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("OLDCODE", PromoCodeDiscountType.Fixed, 100m, 10, 1, 0m, now.AddDays(-10), now.AddDays(-1), PromoCodeScope.AllCourses, null);
        await repo.AddAsync(promo, CancellationToken.None);

        var result = await service.ValidatePromoCodeAsync(Guid.NewGuid(), new ValidatePromoCodeCommand("oldcode", [courseId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsValid);
        Assert.Contains("หมดอายุ", result.Value.ErrorMessage);
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_BelowMinOrderAmount_ReturnsInvalid()
    {
        var repo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var service = new PromoCodeService(repo, catalog, clock);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "Course 1", 400m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("MIN1000", PromoCodeDiscountType.Fixed, 100m, 10, 1, 1000m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await repo.AddAsync(promo, CancellationToken.None);

        var result = await service.ValidatePromoCodeAsync(Guid.NewGuid(), new ValidatePromoCodeCommand("min1000", [courseId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsValid);
        Assert.Contains("ขั้นต่ำ", result.Value.ErrorMessage);
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_MaxPerUserReached_ReturnsInvalid()
    {
        var repo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var service = new PromoCodeService(repo, catalog, clock);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "Course 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("ONCEONLY", PromoCodeDiscountType.Fixed, 100m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await repo.AddAsync(promo, CancellationToken.None);

        // Record 1 redemption
        repo.Redemptions.Add(PROMO_REDEMPTION.Create(promo.PROMO_CODE_ID, Guid.NewGuid(), userId, clock));

        var result = await service.ValidatePromoCodeAsync(userId, new ValidatePromoCodeCommand("onceonly", [courseId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsValid);
        Assert.Contains("ครบตามจำนวนสิทธิ์", result.Value.ErrorMessage);
    }
}
