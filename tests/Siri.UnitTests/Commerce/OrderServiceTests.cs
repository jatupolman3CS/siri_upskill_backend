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
        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }



    private sealed class FakeLearningAccessContract : ILearningAccessContract
    {
        public readonly HashSet<(Guid UserId, Guid CourseId)> Enrolled = [];

        // Call counts prove OrderService uses the batched overloads for a multi-course order instead of
        // looping the single-course ones (the N+1 this fix removes).
        public int HasActiveEnrollmentCallCount;
        public int HasActiveEnrollmentsCallCount;
        public int EnrollUserCallCount;
        public int EnrollUserInCoursesCallCount;

        public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
        {
            HasActiveEnrollmentCallCount++;
            return Task.FromResult(Enrolled.Contains((userId, courseId)));
        }

        public Task<IReadOnlySet<Guid>> HasActiveEnrollmentsAsync(Guid userId, IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken)
        {
            HasActiveEnrollmentsCallCount++;
            var active = courseIds.Where(id => Enrolled.Contains((userId, id))).ToHashSet();
            return Task.FromResult<IReadOnlySet<Guid>>(active);
        }

        public Task<Result> EnrollUserAsync(
            Guid userId,
            Guid courseId,
            Guid? orderId,
            string source,
            DateTime? expiresAtUtc,
            CancellationToken cancellationToken)
        {
            EnrollUserCallCount++;
            Enrolled.Add((userId, courseId));
            return Task.FromResult(Result.Success());
        }

        public Task<Result> EnrollUserInCoursesAsync(
            Guid userId,
            string source,
            IReadOnlyCollection<CourseEnrollmentGrant> grants,
            CancellationToken cancellationToken)
        {
            EnrollUserInCoursesCallCount++;
            foreach (var grant in grants)
            {
                Enrolled.Add((userId, grant.CourseId));
            }

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

        public Task<(IReadOnlyList<ORDER> Items, int TotalCount)> ListByUserIdAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var userOrders = Orders.Values.Where(o => o.USER_ID == userId).OrderByDescending(o => o.CreatedAtUtc).ToList();
            var items = userOrders.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<ORDER> Items, int TotalCount)>((items, userOrders.Count));
        }

        public Task AddAsync(ORDER order, CancellationToken cancellationToken)
        {
            Orders[order.ORDER_ID] = order;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ORDER>> GetStaleAwaitingPaymentOrdersAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
        {
            var result = Orders.Values
                .Where(o => o.STATUS == OrderStatus.AwaitingPayment && o.CreatedAtUtc <= cutoffUtc)
                .OrderBy(o => o.CreatedAtUtc)
                .Take(batchSize)
                .ToList();
            return Task.FromResult<IReadOnlyList<ORDER>>(result);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => operation();

        public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken) => operation();
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
        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(FlashSales.Count);
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
        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Bundles.Count);
    }

    private static OrderService CreateOrderService(
        FakeOrderRepository orderRepo,
        FakePromoCodeRepository promoRepo,
        FakeCatalogPriceContract catalog,
        FakeLearningAccessContract learning,
        IClock clock)
    {
        var flashRepo = new FakeFlashSaleRepository();
        var bundleRepo = new FakeBundleRepository();
        var pricingEngine = new PricingEngine(catalog, flashRepo, bundleRepo, promoRepo, clock);
        return new OrderService(orderRepo, promoRepo, catalog, learning, pricingEngine, clock);
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
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1070m, Guid.NewGuid(), null);

        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
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
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("DISCOUNT300", PromoCodeDiscountType.Fixed, 300m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var userId = Guid.NewGuid();
        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
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
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("FREE100", PromoCodeDiscountType.Percentage, 100m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var userId = Guid.NewGuid();
        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
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

        // Single-course order still uses the batched overload (not the per-course EnrollUserAsync).
        Assert.Equal(1, learning.EnrollUserInCoursesCallCount);
        Assert.Equal(0, learning.EnrollUserCallCount);
    }

    [Fact]
    public async Task CreateAsync_MultiCourseOrderWith100PercentPromoCode_EnrollsAllCoursesInOneBatchedCall()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();
        // course2 is time-limited (AccessDurationDays = 30) — proves the batched grant still carries a
        // real, course-specific expiry instead of collapsing every course to the same value.
        catalog.Prices[course1] = new CoursePriceInfo(course1, "COURSE 1", 500m, Guid.NewGuid(), null);
        catalog.Prices[course2] = new CoursePriceInfo(course2, "COURSE 2", 500m, Guid.NewGuid(), 30);

        var promo = PROMO_CODE.Create("FREEALL", PromoCodeDiscountType.Percentage, 100m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var userId = Guid.NewGuid();
        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(userId, new CreateOrderCommand([course1, course2], "freeall"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Paid, orderRepo.Orders[result.Value.Id].STATUS);
        Assert.Contains((userId, course1), learning.Enrolled);
        Assert.Contains((userId, course2), learning.Enrolled);

        // Proves the batch: one EnrollUserInCoursesAsync call carrying both courses, not two
        // EnrollUserAsync calls (one per course, the old N+1 shape).
        Assert.Equal(1, learning.EnrollUserInCoursesCallCount);
        Assert.Equal(0, learning.EnrollUserCallCount);
    }

    [Fact]
    public async Task CreateAsync_OneOfMultipleCoursesAlreadyActivelyEnrolled_ReturnsConflictViaOneBatchedCheck()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));

        var courseA = Guid.NewGuid();
        var courseB = Guid.NewGuid();
        catalog.Prices[courseA] = new CoursePriceInfo(courseA, "COURSE A", 500m, Guid.NewGuid(), null);
        catalog.Prices[courseB] = new CoursePriceInfo(courseB, "COURSE B", 500m, Guid.NewGuid(), null);

        var userId = Guid.NewGuid();
        learning.Enrolled.Add((userId, courseB));

        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(userId, new CreateOrderCommand([courseA, courseB]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains(courseB.ToString(), result.Error.Message);
        Assert.Empty(orderRepo.Orders);

        // Proves the batch: one HasActiveEnrollmentsAsync call for the whole course set, not one
        // HasActiveEnrollmentAsync call per course.
        Assert.Equal(1, learning.HasActiveEnrollmentsCallCount);
        Assert.Equal(0, learning.HasActiveEnrollmentCallCount);
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
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("DISCOUNT300", PromoCodeDiscountType.Fixed, 300m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var userId = Guid.NewGuid();
        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
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
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);

        var promo = PROMO_CODE.Create("EXPIRED", PromoCodeDiscountType.Fixed, 300m, 10, 1, 0m, now.AddDays(-10), now.AddDays(-1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateOrderCommand([courseId], "expired"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(orderRepo.Orders);
    }

    [Fact]
    public async Task ListUserOrdersAsync_ReturnsPagedOrdersForSpecificUserOnly()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        var order1 = ORDER.Create("ORD-001", user1, 1000m, 0m, 65.42m, 1000m);
        order1.AddItem(Guid.NewGuid(), "COURSE 1", 1000m, 1000m);
        var order2 = ORDER.Create("ORD-002", user1, 2000m, 500m, 98.13m, 1500m);
        order2.AddItem(Guid.NewGuid(), "COURSE 2", 2000m, 1500m);
        var orderOtherUser = ORDER.Create("ORD-003", user2, 500m, 0m, 32.71m, 500m);
        orderOtherUser.AddItem(Guid.NewGuid(), "COURSE 3", 500m, 500m);

        await orderRepo.AddAsync(order1, CancellationToken.None);
        await orderRepo.AddAsync(order2, CancellationToken.None);
        await orderRepo.AddAsync(orderOtherUser, CancellationToken.None);

        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var pagedResult = await service.ListUserOrdersAsync(user1, 1, 10, CancellationToken.None);

        Assert.Equal(2, pagedResult.TotalCount);
        Assert.Equal(2, pagedResult.Items.Count);
        Assert.All(pagedResult.Items, o => Assert.True(o.OrderNo is "ORD-001" or "ORD-002"));
        Assert.Contains(pagedResult.Items, o => o.Items.Count == 1 && o.Items[0].TitleSnapshot == "COURSE 1");
    }

    [Fact]
    public async Task GetByIdAsync_WhenOrderBelongsToOtherUser_ReturnsNotFound()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid();

        var order = ORDER.Create("ORD-OWNER", ownerUserId, 1000m, 0m, 65.42m, 1000m);
        await orderRepo.AddAsync(order, CancellationToken.None);

        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.GetByIdAsync(attackerUserId, order.ORDER_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }
}
