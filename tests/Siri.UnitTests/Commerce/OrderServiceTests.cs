using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Learning.Contracts;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class OrderServiceTests
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

    private sealed class FakeLearningAccessContract : ILearningAccessContract
    {
        public readonly HashSet<(Guid UserId, Guid CourseId)> Enrolled = [];

        public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(Enrolled.Contains((userId, courseId)));

        public Task<Result> EnrollUserAsync(
            Guid userId,
            Guid courseId,
            Guid? orderId,
            string source,
            DateTime? expiresAtUtc,
            CancellationToken cancellationToken)
        {
            Enrolled.Add((userId, courseId));
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public readonly Dictionary<Guid, ORDER> Orders = [];

        public Task<ORDER?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.TryGetValue(orderId, out var order) ? order : null);

        public Task<IReadOnlyList<ORDER>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ORDER>>(Orders.Values.Where(o => o.USER_ID == userId).ToList());

        public Task AddAsync(ORDER order, CancellationToken cancellationToken)
        {
            Orders[order.ORDER_ID] = order;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => operation();
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
    public async Task CreateAsync_WithoutPromoCode_CreatesOrderWithZeroDiscount()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "Course 1", 1070m, Guid.NewGuid(), null);

        var service = new OrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateOrderCommand([courseId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var order = orderRepo.Orders[result.Value.Id];
        Assert.Equal(1070m, order.SUBTOTAL_AMOUNT);
        Assert.Equal(0m, order.DISCOUNT_AMOUNT);
        Assert.Equal(70m, order.TAX_AMOUNT);
        Assert.Equal(1070m, order.TOTAL_AMOUNT);
        Assert.Null(order.PROMO_CODE_ID);
        Assert.Empty(promoRepo.Redemptions);
    }

    [Fact]
    public async Task CreateAsync_WithValidFixedPromoCode_AppliesDiscountAndRedeems()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "Course 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("DISCOUNT300", PromoCodeDiscountType.Fixed, 300m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var userId = Guid.NewGuid();
        var service = new OrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(userId, new CreateOrderCommand([courseId], "discount300"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var order = orderRepo.Orders[result.Value.Id];
        Assert.Equal(1000m, order.SUBTOTAL_AMOUNT);
        Assert.Equal(300m, order.DISCOUNT_AMOUNT);
        Assert.Equal(700m, order.TOTAL_AMOUNT);
        Assert.Equal(Math.Round(700m * 7m / 107m, 2), order.TAX_AMOUNT);
        Assert.Equal(promo.PROMO_CODE_ID, order.PROMO_CODE_ID);
        Assert.Single(promoRepo.Redemptions);
        Assert.Equal(promo.PROMO_CODE_ID, promoRepo.Redemptions[0].PROMO_CODE_ID);
        Assert.Equal(order.ORDER_ID, promoRepo.Redemptions[0].ORDER_ID);
        Assert.Equal(userId, promoRepo.Redemptions[0].USER_ID);
    }

    [Fact]
    public async Task CreateAsync_With100PercentPromoCode_ImmediatelyMarksPaidAndEnrolls()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "Course 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("FREE100", PromoCodeDiscountType.Percentage, 100m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var userId = Guid.NewGuid();
        var service = new OrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(userId, new CreateOrderCommand([courseId], "free100"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var order = orderRepo.Orders[result.Value.Id];
        Assert.Equal(1000m, order.SUBTOTAL_AMOUNT);
        Assert.Equal(1000m, order.DISCOUNT_AMOUNT);
        Assert.Equal(0m, order.TOTAL_AMOUNT);
        Assert.Equal(0m, order.TAX_AMOUNT);
        Assert.Equal(OrderStatus.Paid, order.STATUS);
        Assert.NotNull(order.PAID_AT_UTC);
        Assert.Contains((userId, courseId), learning.Enrolled);
    }

    [Fact]
    public async Task CancelAsync_WithPromoCode_RevertsRedemption()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "Course 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("DISCOUNT300", PromoCodeDiscountType.Fixed, 300m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var userId = Guid.NewGuid();
        var service = new OrderService(orderRepo, promoRepo, catalog, learning, clock);
        var createResult = await service.CreateAsync(userId, new CreateOrderCommand([courseId], "discount300"), CancellationToken.None);

        Assert.True(createResult.IsSuccess);
        Assert.Single(promoRepo.Redemptions);

        var cancelResult = await service.CancelAsync(userId, createResult.Value.Id, CancellationToken.None);
        Assert.True(cancelResult.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, cancelResult.Value.Status);
        Assert.Empty(promoRepo.Redemptions);
    }

    [Fact]
    public async Task CreateAsync_WithExpiredPromoCode_ReturnsValidationError()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "Course 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("EXPIRED", PromoCodeDiscountType.Fixed, 300m, 10, 1, 0m, now.AddDays(-10), now.AddDays(-1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var service = new OrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateOrderCommand([courseId], "expired"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(orderRepo.Orders);
    }
}
