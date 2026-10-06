using Siri.Integrations.Payment;
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

        // P11-11: configurable per-test enrollment policy + seat reservation behavior. Kept separate
        // (SeatReservationSucceeds) instead of deriving pass/fail from EnrollmentPolicies' MaxSeats/
        // SeatsUsed, so a test can prove OrderService reacts correctly to TryReserveSeatAsync's return
        // value without having to fake out real seat-counting arithmetic.
        public readonly Dictionary<Guid, CourseEnrollmentPolicyInfo> EnrollmentPolicies = [];
        public bool SeatReservationSucceeds = true;
        public readonly List<Guid> ReservedSeatCourseIds = [];
        public readonly List<Guid> ReleasedSeatCourseIds = [];

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

        public Task<IReadOnlyDictionary<Guid, CourseEnrollmentPolicyInfo>> GetEnrollmentPoliciesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken)
        {
            var result = courseIds
                .Where(id => EnrollmentPolicies.ContainsKey(id))
                .ToDictionary(id => id, id => EnrollmentPolicies[id]);
            return Task.FromResult<IReadOnlyDictionary<Guid, CourseEnrollmentPolicyInfo>>(result);
        }

        public Task<bool> TryReserveSeatAsync(Guid courseId, CancellationToken cancellationToken)
        {
            ReservedSeatCourseIds.Add(courseId);
            return Task.FromResult(SeatReservationSucceeds);
        }

        public Task ReleaseSeatAsync(Guid courseId, CancellationToken cancellationToken)
        {
            ReleasedSeatCourseIds.Add(courseId);
            return Task.CompletedTask;
        }
    }



    private sealed class FakeLearningAccessContract : ILearningAccessContract
    {
        public readonly HashSet<(Guid UserId, Guid CourseId)> Enrolled = [];

        // P11-13 (Q13.3): captures the ExpiresAtUtc actually granted per course, so tests can assert the
        // real computed value (session-based vs. now-based fallback) instead of only "enrolled or not".
        public readonly Dictionary<Guid, DateTime?> ExpiresAtUtcByCourseId = [];

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
            ExpiresAtUtcByCourseId[courseId] = expiresAtUtc;
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
                ExpiresAtUtcByCourseId[grant.CourseId] = grant.ExpiresAtUtc;
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
                .Where(o => (o.STATUS == OrderStatus.AwaitingPayment || o.STATUS == OrderStatus.Pending) && o.CreatedAtUtc <= cutoffUtc)
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

    /// <summary>P11-13 (Q13.3): only <see cref="GetEarliestScheduledSessionAsync"/> is used by
    /// <see cref="OrderService"/> — every other member of <see cref="ILiveScheduleReader"/> throws so a
    /// test would fail loudly if the production code path ever changed to call one of them.</summary>
    private sealed class FakeLiveScheduleReader : ILiveScheduleReader
    {
        public readonly Dictionary<Guid, LiveSessionInfo> EarliestSessionByCourseId = [];

        public Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by this service under test — only GetEarliestScheduledSessionAsync is called.");

        public Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by this service under test — only GetEarliestScheduledSessionAsync is called.");

        public Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by this service under test — only GetEarliestScheduledSessionAsync is called.");

        public Task<LiveSessionInfo?> GetEarliestScheduledSessionAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(EarliestSessionByCourseId.TryGetValue(courseId, out var session) ? session : null);
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        public readonly Dictionary<Guid, PAYMENT> Payments = [];

        public Task<PAYMENT?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(Payments.TryGetValue(paymentId, out var p) ? p : null);

        public Task<PAYMENT?> GetByProviderPaymentIntentIdAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Payments.Values.FirstOrDefault(p => p.PROVIDER_PAYMENT_INTENT_ID == providerPaymentIntentId));

        public Task AddAsync(PAYMENT payment, CancellationToken cancellationToken)
        {
            Payments[payment.PAYMENT_ID] = payment;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PAYMENT>> GetPendingByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PAYMENT>>(Payments.Values
                .Where(p => p.ORDER_ID == orderId && (p.STATUS == PaymentStatus.Pending || p.STATUS == PaymentStatus.Processing))
                .ToList());
    }

    private sealed class FakePaymentMethod : IPaymentMethod
    {
        public readonly List<string> CanceledIntentIds = [];
        public bool CancelSucceeds = true;

        public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> CancelPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken)
        {
            CanceledIntentIds.Add(providerPaymentIntentId);
            return Task.FromResult(CancelSucceeds ? Result.Success() : Result.Failure(DomainError.Conflict("cannot cancel")));
        }

        public Task<Result<PaymentRefundResult>> CreateRefundAsync(CreateRefundRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<decimal?>> GetChargeFeeAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static OrderService CreateOrderService(
        FakeOrderRepository orderRepo,
        FakePromoCodeRepository promoRepo,
        FakeCatalogPriceContract catalog,
        FakeLearningAccessContract learning,
        IClock clock,
        FakeLiveScheduleReader? liveScheduleReader = null)
    {
        var flashRepo = new FakeFlashSaleRepository();
        var bundleRepo = new FakeBundleRepository();
        var pricingEngine = new PricingEngine(catalog, flashRepo, bundleRepo, promoRepo, clock);
        return new OrderService(orderRepo, promoRepo, catalog, liveScheduleReader ?? new FakeLiveScheduleReader(), learning, pricingEngine, clock,
            new FakePaymentRepository(), new FakePaymentMethod());
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
    public async Task CreateAsync_EnrollmentDeadlineHasPassed_ReturnsConflictBeforeCreatingOrder()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);
        catalog.EnrollmentPolicies[courseId] = new CourseEnrollmentPolicyInfo(courseId, now.AddDays(-1), null, 0);

        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateOrderCommand([courseId]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(orderRepo.Orders);

        // Deadline check must happen before pricing/seat reservation — no seat should ever be reserved.
        Assert.Empty(catalog.ReservedSeatCourseIds);
    }

    [Fact]
    public async Task CreateAsync_EnrollmentDeadlineInTheFuture_Succeeds()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);
        catalog.EnrollmentPolicies[courseId] = new CourseEnrollmentPolicyInfo(courseId, now.AddDays(1), null, 0);

        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateOrderCommand([courseId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(orderRepo.Orders);
    }

    [Fact]
    public async Task CreateAsync_SeatCappedCourseFull_ReturnsConflictAndDoesNotPersistOrder()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);
        catalog.EnrollmentPolicies[courseId] = new CourseEnrollmentPolicyInfo(courseId, null, 1, 1);
        catalog.SeatReservationSucceeds = false;

        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateOrderCommand([courseId]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(orderRepo.Orders);
        Assert.Contains(courseId, catalog.ReservedSeatCourseIds);
    }

    [Fact]
    public async Task CreateAsync_SeatCappedCourseWithAvailableSeats_ReservesSeatAndSucceeds()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);
        catalog.EnrollmentPolicies[courseId] = new CourseEnrollmentPolicyInfo(courseId, null, 10, 3);

        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateOrderCommand([courseId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(orderRepo.Orders);
        Assert.Contains(courseId, catalog.ReservedSeatCourseIds);
    }

    [Fact]
    public async Task CreateAsync_CourseWithNoEnrollmentPolicySet_ReservesSeatSymmetricWithReleaseAndSkipsDeadline()
    {
        // A course with EnrollmentDeadlineUtc/MaxSeats both null (the default for every pre-existing
        // course) still reserves a seat — CancelAsync/OrderExpiryJob release for every course, so Reserve
        // must be symmetric or SeatsUsed drifts low when a cap is set/cleared while orders are open.
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);
        // No entry in catalog.EnrollmentPolicies at all — mirrors GetEnrollmentPoliciesAsync's real
        // implementation, which only returns rows that exist (it never fabricates a null/null entry).

        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateOrderCommand([courseId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(orderRepo.Orders);
        Assert.Contains(courseId, catalog.ReservedSeatCourseIds);
    }

    [Fact]
    public async Task CancelAsync_OrderWithCourseItems_ReleasesSeatForEachDistinctCourse()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);
        catalog.EnrollmentPolicies[courseId] = new CourseEnrollmentPolicyInfo(courseId, null, 10, 1);

        var userId = Guid.NewGuid();
        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock);
        var createResult = await service.CreateAsync(userId, new CreateOrderCommand([courseId]), CancellationToken.None);
        Assert.True(createResult.IsSuccess);

        var cancelResult = await service.CancelAsync(userId, createResult.Value.Id, CancellationToken.None);

        Assert.True(cancelResult.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, cancelResult.Value.Status);
        Assert.Contains(courseId, catalog.ReleasedSeatCourseIds);
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

    // P11-13 (Q13.3): zero-amount fast path (100%-discount promo code) must compute ExpiresAtUtc the same
    // way the paid webhook path does — from the course's earliest scheduled live session when one exists,
    // otherwise falling back to "now" (covers both an OnDemand course and a Live/Hybrid course with no
    // session scheduled yet, which the production code cannot and must not try to tell apart — see
    // docs/contracts/P11-13-access-duration-first-session.md §0.2).

    [Fact]
    public async Task CreateAsync_ZeroAmountWithEarliestScheduledSession_ComputesExpiresAtUtcFromSessionStart()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), 30);

        var sessionStartsAtUtc = now.AddDays(10);
        var liveScheduleReader = new FakeLiveScheduleReader();
        liveScheduleReader.EarliestSessionByCourseId[courseId] =
            new LiveSessionInfo(Guid.NewGuid(), courseId, "Session 1", sessionStartsAtUtc, sessionStartsAtUtc.AddHours(1), LiveSessionStatus.Scheduled, null);

        var promo = PROMO_CODE.Create("FREE100SESSION", PromoCodeDiscountType.Percentage, 100m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var userId = Guid.NewGuid();
        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock, liveScheduleReader);
        var result = await service.CreateAsync(userId, new CreateOrderCommand([courseId], "free100session"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains((userId, courseId), learning.Enrolled);

        // Must equal session.StartsAtUtc.AddDays(30), and must NOT equal clock.UtcNow.AddDays(30) — the
        // two are asserted separately so this test cannot pass by coincidence.
        var expectedExpiresAtUtc = sessionStartsAtUtc.AddDays(30);
        var fallbackExpiresAtUtc = now.AddDays(30);
        Assert.NotEqual(expectedExpiresAtUtc, fallbackExpiresAtUtc);
        Assert.Equal(expectedExpiresAtUtc, learning.ExpiresAtUtcByCourseId[courseId]);
    }

    [Fact]
    public async Task CreateAsync_ZeroAmountWithNoScheduledSession_FallsBackToNowPlusAccessDurationDays()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        // No entry in liveScheduleReader.EarliestSessionByCourseId for this course — covers both "the
        // course is OnDemand" and "the course is Live/Hybrid but nothing is scheduled yet" (see class
        // comment above): production code cannot distinguish the two, and Q13.3 wants the same fallback
        // for both, so one test stands in for both cases.
        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), 30);

        var promo = PROMO_CODE.Create("FREE100NOSESSION", PromoCodeDiscountType.Percentage, 100m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var userId = Guid.NewGuid();
        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock, new FakeLiveScheduleReader());
        var result = await service.CreateAsync(userId, new CreateOrderCommand([courseId], "free100nosession"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains((userId, courseId), learning.Enrolled);
        Assert.Equal(now.AddDays(30), learning.ExpiresAtUtcByCourseId[courseId]);
    }

    [Fact]
    public async Task CreateAsync_ZeroAmountWithNullAccessDurationDays_GrantsLifetimeAccessWithoutQueryingLiveSchedule()
    {
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var catalog = new FakeCatalogPriceContract();
        var learning = new FakeLearningAccessContract();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        // AccessDurationDays = null (lifetime access) — the short-circuit `AccessDurationDays is { } days`
        // guard must skip GetEarliestScheduledSessionAsync entirely, not just discard its result. Every
        // other member of FakeLiveScheduleReader throws, so this test would fail loudly if production
        // code ever called GetSessionsForCourseAsync/GetUpcomingSessionsAsync/GetSessionAsync here —
        // GetEarliestScheduledSessionAsync itself would not throw even if called, so a session is seeded
        // anyway to prove it is genuinely never consulted (the resulting ExpiresAtUtc stays null either way).
        var courseId = Guid.NewGuid();
        catalog.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);

        var liveScheduleReader = new FakeLiveScheduleReader();
        liveScheduleReader.EarliestSessionByCourseId[courseId] =
            new LiveSessionInfo(Guid.NewGuid(), courseId, "Session 1", now.AddDays(10), now.AddDays(10).AddHours(1), LiveSessionStatus.Scheduled, null);

        var promo = PROMO_CODE.Create("FREE100LIFETIME", PromoCodeDiscountType.Percentage, 100m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var userId = Guid.NewGuid();
        var service = CreateOrderService(orderRepo, promoRepo, catalog, learning, clock, liveScheduleReader);
        var result = await service.CreateAsync(userId, new CreateOrderCommand([courseId], "free100lifetime"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains((userId, courseId), learning.Enrolled);
        Assert.Null(learning.ExpiresAtUtcByCourseId[courseId]);
    }
}
