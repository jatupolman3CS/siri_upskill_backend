using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Payment;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class OrderExpiryJobTests
{
    private readonly FakeOrderRepository _orderRepo = new();
    private readonly FakePaymentRepository _paymentRepo = new();
    private readonly FakePaymentMethod _paymentMethod = new();
    private readonly FakePromoCodeRepository _promoCodeRepo = new();
    private readonly FakeCatalogPriceContract _catalogPriceContract = new();
    private readonly MutableClock _clock = new(new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc));

    private OrderExpiryJob CreateJob(int expiryMinutes = 30, int batchSize = 50)
    {
        var options = Options.Create(new OrderExpiryOptions
        {
            ExpiryMinutes = expiryMinutes,
            BatchSize = batchSize
        });

        return new OrderExpiryJob(
            _orderRepo,
            _paymentRepo,
            _paymentMethod,
            _promoCodeRepo,
            _catalogPriceContract,
            _clock,
            options,
            NullLogger<OrderExpiryJob>.Instance);
    }

    [Fact]
    public async Task RunAsync_ExpiredOrderWithCourseItems_ReleasesSeatForEachDistinctCourse()
    {
        var userId = Guid.NewGuid();
        var courseId1 = Guid.NewGuid();
        var courseId2 = Guid.NewGuid();

        var order = ORDER.Create("ORD-SEAT-EXP-1", userId, 2000m, 0m, 130.84m, 2000m);
        order.AddItem(courseId1, "COURSE 1", 1000m, 1000m);
        order.AddItem(courseId2, "COURSE 2", 1000m, 1000m);
        order.MarkAwaitingPayment();
        typeof(ORDER).GetProperty(nameof(ORDER.CreatedAtUtc))!.SetValue(order, _clock.UtcNow.AddMinutes(-35));
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var job = CreateJob(expiryMinutes: 30);
        await job.RunAsync(CancellationToken.None);

        var updatedOrder = await _orderRepo.GetByIdAsync(order.ORDER_ID, CancellationToken.None);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Cancelled, updatedOrder.STATUS);

        Assert.Contains(courseId1, _catalogPriceContract.ReleasedSeatCourseIds);
        Assert.Contains(courseId2, _catalogPriceContract.ReleasedSeatCourseIds);
        Assert.Equal(2, _catalogPriceContract.ReleasedSeatCourseIds.Count);
    }

    [Fact]
    public async Task RunAsync_ExpiredAwaitingPaymentOrder_CancelsOrderAndPaymentAndRevertsPromo()
    {
        var userId = Guid.NewGuid();
        var promoId = Guid.NewGuid();
        var promo = PROMO_CODE.Create(
            "SAVE50",
            PromoCodeDiscountType.Fixed,
            50m,
            100,
            1,
            100m,
            _clock.UtcNow.AddDays(-1),
            _clock.UtcNow.AddDays(1),
            PromoCodeScope.AllCourses,
            null);
        typeof(PROMO_CODE).GetProperty(nameof(PROMO_CODE.PROMO_CODE_ID))!.SetValue(promo, promoId);
        _promoCodeRepo.Codes[promoId] = promo;

        // Order created 35 minutes ago (exceeding 30 min cutoff)
        var order = ORDER.Create("ORD-EXP-1", userId, 1000m, 50m, 62.15m, 950m, promoId);
        order.MarkAwaitingPayment();
        typeof(ORDER).GetProperty(nameof(ORDER.CreatedAtUtc))!.SetValue(order, _clock.UtcNow.AddMinutes(-35));
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_test_exp_1", 950m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var job = CreateJob(expiryMinutes: 30);
        await job.RunAsync(CancellationToken.None);

        var updatedOrder = await _orderRepo.GetByIdAsync(order.ORDER_ID, CancellationToken.None);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Cancelled, updatedOrder.STATUS);

        var updatedPayment = await _paymentRepo.GetByIdAsync(payment.PAYMENT_ID, CancellationToken.None);
        Assert.NotNull(updatedPayment);
        Assert.Equal(PaymentStatus.Expired, updatedPayment.STATUS);

        Assert.Contains("pi_test_exp_1", _paymentMethod.CanceledIntentIds);
        Assert.Contains(order.ORDER_ID, _promoCodeRepo.RevertedOrderIds);
    }

    [Fact]
    public async Task RunAsync_FreshOrderWithinCutoff_IsNotExpired()
    {
        var userId = Guid.NewGuid();
        // Order created only 10 minutes ago
        var order = ORDER.Create("ORD-FRESH-1", userId, 500m, 0m, 0m, 500m);
        order.MarkAwaitingPayment();
        typeof(ORDER).GetProperty(nameof(ORDER.CreatedAtUtc))!.SetValue(order, _clock.UtcNow.AddMinutes(-10));
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_test_fresh_1", 500m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var job = CreateJob(expiryMinutes: 30);
        await job.RunAsync(CancellationToken.None);

        var updatedOrder = await _orderRepo.GetByIdAsync(order.ORDER_ID, CancellationToken.None);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.AwaitingPayment, updatedOrder.STATUS);

        var updatedPayment = await _paymentRepo.GetByIdAsync(payment.PAYMENT_ID, CancellationToken.None);
        Assert.NotNull(updatedPayment);
        Assert.Equal(PaymentStatus.Pending, updatedPayment.STATUS);

        Assert.DoesNotContain("pi_test_fresh_1", _paymentMethod.CanceledIntentIds);
    }

    [Fact]
    public async Task RunAsync_AlreadyPaidOrder_IsNotTouched()
    {
        var userId = Guid.NewGuid();
        var order = ORDER.Create("ORD-PAID-1", userId, 500m, 0m, 0m, 500m);
        order.MarkAwaitingPayment();
        order.MarkPaid(_clock);
        typeof(ORDER).GetProperty(nameof(ORDER.CreatedAtUtc))!.SetValue(order, _clock.UtcNow.AddMinutes(-60));
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_test_paid_1", 500m, _clock);
        payment.MarkSucceeded(_clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var job = CreateJob(expiryMinutes: 30);
        await job.RunAsync(CancellationToken.None);

        var updatedOrder = await _orderRepo.GetByIdAsync(order.ORDER_ID, CancellationToken.None);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Paid, updatedOrder.STATUS);

        var updatedPayment = await _paymentRepo.GetByIdAsync(payment.PAYMENT_ID, CancellationToken.None);
        Assert.NotNull(updatedPayment);
        Assert.Equal(PaymentStatus.Succeeded, updatedPayment.STATUS);
    }

    [Fact]
    public async Task RunAsync_IdempotentOnRepeatedRuns()
    {
        var userId = Guid.NewGuid();
        var order = ORDER.Create("ORD-IDEM-1", userId, 1000m, 0m, 0m, 1000m);
        order.MarkAwaitingPayment();
        typeof(ORDER).GetProperty(nameof(ORDER.CreatedAtUtc))!.SetValue(order, _clock.UtcNow.AddMinutes(-40));
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_test_idem_1", 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var job = CreateJob(expiryMinutes: 30);

        // First run expires the order
        await job.RunAsync(CancellationToken.None);
        Assert.Single(_paymentMethod.CanceledIntentIds);

        // Second run finds no pending orders
        await job.RunAsync(CancellationToken.None);
        Assert.Single(_paymentMethod.CanceledIntentIds); // Still only 1 cancel call
    }

    private sealed class MutableClock(DateTime initial) : IClock
    {
        public DateTime UtcNow { get; set; } = initial;
    }

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        public readonly List<Guid> ReleasedSeatCourseIds = [];

        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(new Dictionary<Guid, CoursePriceInfo>());

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());

        public Task ReleaseSeatAsync(Guid courseId, CancellationToken cancellationToken)
        {
            ReleasedSeatCourseIds.Add(courseId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentMethod : IPaymentMethod
    {
        public readonly List<string> CanceledIntentIds = [];

        public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new PaymentIntentResult("pi_fake", "secret", "requires_payment_method", request.Amount, "thb", null, null)));

        public Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new PaymentIntentResult(providerPaymentIntentId, "secret", "succeeded", 100m, "thb", null, null)));

        public Task<Result> CancelPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken)
        {
            CanceledIntentIds.Add(providerPaymentIntentId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<PaymentRefundResult>> CreateRefundAsync(CreateRefundRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new PaymentRefundResult("re_test", "succeeded", request.Amount, "thb")));

        public Task<Result<decimal?>> GetChargeFeeAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<decimal?>(null));
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

        public Task<IReadOnlyList<ORDER>> GetStaleAwaitingPaymentOrdersAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
        {
            var result = Orders.Values
                .Where(o => (o.STATUS == OrderStatus.AwaitingPayment || o.STATUS == OrderStatus.Pending) && o.CreatedAtUtc <= cutoffUtc)
                .OrderBy(o => o.CreatedAtUtc)
                .Take(batchSize)
                .ToList();
            return Task.FromResult<IReadOnlyList<ORDER>>(result);
        }

        public Task AddAsync(ORDER order, CancellationToken cancellationToken)
        {
            Orders[order.ORDER_ID] = order;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => operation();

        public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken) => operation();
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

        public Task<IReadOnlyList<PAYMENT>> GetPendingByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
        {
            var result = Payments.Values
                .Where(p => p.ORDER_ID == orderId && (p.STATUS == PaymentStatus.Pending || p.STATUS == PaymentStatus.Processing))
                .ToList();
            return Task.FromResult<IReadOnlyList<PAYMENT>>(result);
        }
    }

    private sealed class FakePromoCodeRepository : IPromoCodeRepository
    {
        public readonly Dictionary<Guid, PROMO_CODE> Codes = [];
        public readonly List<Guid> RevertedOrderIds = [];

        public Task<PROMO_CODE?> GetByIdAsync(Guid promoCodeId, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.TryGetValue(promoCodeId, out var code) ? code : null);

        public Task<PROMO_CODE?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Values.FirstOrDefault(p => p.CODE.Equals(code, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(PROMO_CODE promoCode, CancellationToken cancellationToken)
        {
            Codes[promoCode.PROMO_CODE_ID] = promoCode;
            return Task.CompletedTask;
        }

        public Task<bool> TryRedeemAsync(Guid promoCodeId, Guid orderId, Guid userId, int maxPerUser, IClock clock, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<int> GetUserRedemptionCountAsync(Guid promoCodeId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task RevertRedemptionAsync(Guid promoCodeId, Guid orderId, CancellationToken cancellationToken)
        {
            RevertedOrderIds.Add(orderId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PROMO_CODE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PROMO_CODE>>(Codes.Values.ToList());

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Count);
    }
}
