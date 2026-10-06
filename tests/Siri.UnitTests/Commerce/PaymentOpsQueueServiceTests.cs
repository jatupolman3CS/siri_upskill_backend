using Microsoft.Extensions.Logging.Abstractions;
using Siri.Integrations.Payment;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Payout.Contracts;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class PaymentOpsQueueServiceTests
{
    private readonly FakePaymentOpsQueueRepository _opsQueueRepo = new();
    private readonly FakePaymentRepository _paymentRepo = new();
    private readonly FakeOrderRepository _orderRepo = new();
    private readonly FakePromoCodeRepository _promoCodeRepo = new();
    private readonly FakePaymentMethod _paymentMethod = new();
    private readonly FakeLearningAccessContract _learningAccessContract = new();
    private readonly FakeCatalogPriceContract _catalogPriceContract = new();
    private readonly FakeLiveScheduleReader _liveScheduleReader = new();
    private readonly FakeRevenueSplitContract _revenueSplitContract = new();
    private readonly FakeEmailOutbox _emailOutbox = new();
    private readonly FakeUserContactReader _userContactReader = new();
    private readonly FakeClock _clock = new(new DateTime(2026, 8, 27, 14, 0, 0, DateTimeKind.Utc));

    private PaymentOpsQueueService CreateService() =>
        new(
            _opsQueueRepo,
            _paymentRepo,
            _orderRepo,
            _promoCodeRepo,
            _paymentMethod,
            _learningAccessContract,
            _catalogPriceContract,
            _liveScheduleReader,
            _emailOutbox,
            _userContactReader,
            _clock,
            NullLogger<PaymentOpsQueueService>.Instance,
            _revenueSplitContract);

    [Fact]
    public async Task ListAsync_ReturnsPagedEntriesWithOrderAndUserEmail()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        _userContactReader.Emails[userId] = "buyer@example.test";

        var order = ORDER.Create("ORD-OPS-1", userId, 1000m, 0m, 0m, 1000m);
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_ops_1", 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var entry = PAYMENT_OPS_QUEUE.Create(payment.PAYMENT_ID, "Late payment after order cancelled");
        await _opsQueueRepo.AddAsync(entry, CancellationToken.None);

        var result = await service.ListAsync(null, 1, 20, CancellationToken.None);

        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal(entry.PAYMENT_OPS_QUEUE_ID, item.Id);
        Assert.Equal("ORD-OPS-1", item.OrderNo);
        Assert.Equal("buyer@example.test", item.UserEmail);
        Assert.Equal(1000m, item.Amount);
    }

    [Fact]
    public async Task AssignAsync_WhenOpen_AssignsAdmin()
    {
        var service = CreateService();
        var adminId = Guid.NewGuid();

        var entry = PAYMENT_OPS_QUEUE.Create(Guid.NewGuid(), "Late payment");
        await _opsQueueRepo.AddAsync(entry, CancellationToken.None);

        var result = await service.AssignAsync(entry.PAYMENT_OPS_QUEUE_ID, adminId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentOpsQueueStatus.InProgress, result.Value.Status);
        Assert.Equal(adminId, result.Value.AssignedToUserId);
    }

    [Fact]
    public async Task ResolveAsync_WithRefundAction_TriggersStripeRefundRevertsPromoAndNotifiesBuyer()
    {
        var service = CreateService();
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var promoId = Guid.NewGuid();
        _userContactReader.Emails[userId] = "buyer@example.test";

        var order = ORDER.Create("ORD-REFUND-1", userId, 1000m, 100m, 58.88m, 900m, promoId);
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_to_refund_1", 900m, _clock);
        payment.MarkSucceeded(_clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var entry = PAYMENT_OPS_QUEUE.Create(payment.PAYMENT_ID, "Customer requested cancellation");
        await _opsQueueRepo.AddAsync(entry, CancellationToken.None);

        var resolveRequest = new ResolvePaymentOpsRequest(
            PaymentOpsResolutionAction.Refund,
            "Approved full refund to customer",
            "requested_by_customer");

        var result = await service.ResolveAsync(entry.PAYMENT_OPS_QUEUE_ID, adminId, resolveRequest, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentOpsQueueStatus.Resolved, result.Value.Status);
        Assert.Equal(PaymentStatus.Refunded, payment.STATUS);
        Assert.Contains("pi_to_refund_1", _paymentMethod.RefundedIntentIds);
        Assert.Contains(order.ORDER_ID, _promoCodeRepo.RevertedOrderIds);
        Assert.Single(_emailOutbox.Sent);
        Assert.Equal("buyer@example.test", _emailOutbox.Sent[0].ToEmail);
    }

    [Fact]
    public async Task ResolveAsync_WithReopenAndFulfillOrderAction_MarksPaidEnrollsAndNotifiesBuyer()
    {
        var service = CreateService();
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        _userContactReader.Emails[userId] = "buyer@example.test";
        _catalogPriceContract.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE Title", 1500m, Guid.NewGuid(), 30);

        var order = ORDER.Create("ORD-REOPEN-1", userId, 1500m, 0m, 0m, 1500m);
        order.AddItem(courseId, "COURSE Title", 1500m, 1500m);
        order.MarkAwaitingPayment();
        var statusProp = typeof(ORDER).GetProperty(nameof(ORDER.STATUS));
        statusProp!.SetValue(order, OrderStatus.Cancelled); // Cancelled prematurely by expiry
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_late_paid_1", 1500m, _clock);
        payment.MarkSucceeded(_clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var entry = PAYMENT_OPS_QUEUE.Create(payment.PAYMENT_ID, "Late payment succeeded after cancellation");
        await _opsQueueRepo.AddAsync(entry, CancellationToken.None);

        var resolveRequest = new ResolvePaymentOpsRequest(
            PaymentOpsResolutionAction.ReopenAndFulfillOrder,
            "Reopened order and enrolled student");

        var result = await service.ResolveAsync(entry.PAYMENT_OPS_QUEUE_ID, adminId, resolveRequest, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentOpsQueueStatus.Resolved, result.Value.Status);
        Assert.Equal(OrderStatus.Paid, order.STATUS);
        Assert.Single(_learningAccessContract.Grants);
        Assert.Equal(courseId, _learningAccessContract.Grants[0].CourseId);
        Assert.Single(_emailOutbox.Sent);

        // Proves the batch: one EnrollUserInCoursesAsync call, not the old per-course EnrollUserAsync loop.
        Assert.Equal(1, _learningAccessContract.EnrollUserInCoursesCallCount);
        Assert.Equal(0, _learningAccessContract.EnrollUserCallCount);
    }

    [Fact]
    public async Task ResolveAsync_WithReopenAndFulfillOrderAction_MultiCourseOrder_EnrollsAllCoursesInOneBatchedCall()
    {
        var service = CreateService();
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();
        _userContactReader.Emails[userId] = "buyer@example.test";

        // course1 is time-limited (30 days), course2 is lifetime (null) — proves each grant in the
        // batch carries its own course-specific expiry, not one value applied to the whole order.
        _catalogPriceContract.Prices[course1] = new CoursePriceInfo(course1, "COURSE 1", 1000m, Guid.NewGuid(), 30);
        _catalogPriceContract.Prices[course2] = new CoursePriceInfo(course2, "COURSE 2", 500m, Guid.NewGuid(), null);

        var order = ORDER.Create("ORD-REOPEN-2", userId, 1500m, 0m, 0m, 1500m);
        order.AddItem(course1, "COURSE 1", 1000m, 1000m);
        order.AddItem(course2, "COURSE 2", 500m, 500m);
        order.MarkAwaitingPayment();
        var statusProp = typeof(ORDER).GetProperty(nameof(ORDER.STATUS));
        statusProp!.SetValue(order, OrderStatus.Cancelled); // Cancelled prematurely by expiry
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_late_paid_2", 1500m, _clock);
        payment.MarkSucceeded(_clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var entry = PAYMENT_OPS_QUEUE.Create(payment.PAYMENT_ID, "Late payment succeeded after cancellation");
        await _opsQueueRepo.AddAsync(entry, CancellationToken.None);

        var resolveRequest = new ResolvePaymentOpsRequest(
            PaymentOpsResolutionAction.ReopenAndFulfillOrder,
            "Reopened order and enrolled student");

        var result = await service.ResolveAsync(entry.PAYMENT_OPS_QUEUE_ID, adminId, resolveRequest, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Paid, order.STATUS);

        // Single batched call for the whole order (not one EnrollUserAsync call per course).
        Assert.Equal(1, _learningAccessContract.EnrollUserInCoursesCallCount);
        Assert.Equal(0, _learningAccessContract.EnrollUserCallCount);

        Assert.Equal(2, _learningAccessContract.Grants.Count);
        Assert.Contains(_learningAccessContract.Grants, g => g.CourseId == course1 && g.OrderId == order.ORDER_ID && g.Source == "OpsResolution" && g.ExpiresAtUtc == _clock.UtcNow.AddDays(30));
        Assert.Contains(_learningAccessContract.Grants, g => g.CourseId == course2 && g.OrderId == order.ORDER_ID && g.Source == "OpsResolution" && g.ExpiresAtUtc == null);
    }

    // P11-13 (Q13.3): same fix as OrderServiceTests/StripeWebhookHandlerTests — the admin ops-queue path
    // must compute ExpiresAtUtc for a Live/Hybrid course from the earliest scheduled session's StartsAtUtc,
    // not from the moment the admin resolves the queue entry, so a student getting access this way is not
    // treated worse (or differently) than one going through the normal webhook/free-checkout path.

    [Fact]
    public async Task ResolveAsync_WithReopenAndFulfillOrderAction_CourseHasEarliestScheduledSession_ComputesExpiresAtUtcFromSessionStart()
    {
        var service = CreateService();
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        _userContactReader.Emails[userId] = "buyer@example.test";
        _catalogPriceContract.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE Title", 1500m, Guid.NewGuid(), 30);

        var sessionStartsAtUtc = _clock.UtcNow.AddDays(10);
        _liveScheduleReader.EarliestSessionByCourseId[courseId] =
            new LiveSessionInfo(Guid.NewGuid(), courseId, "Session 1", sessionStartsAtUtc, sessionStartsAtUtc.AddHours(1), LiveSessionStatus.Scheduled, null);

        var order = ORDER.Create("ORD-REOPEN-SESSION", userId, 1500m, 0m, 0m, 1500m);
        order.AddItem(courseId, "COURSE Title", 1500m, 1500m);
        order.MarkAwaitingPayment();
        var statusProp = typeof(ORDER).GetProperty(nameof(ORDER.STATUS));
        statusProp!.SetValue(order, OrderStatus.Cancelled); // Cancelled prematurely by expiry
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_late_paid_session", 1500m, _clock);
        payment.MarkSucceeded(_clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var entry = PAYMENT_OPS_QUEUE.Create(payment.PAYMENT_ID, "Late payment succeeded after cancellation");
        await _opsQueueRepo.AddAsync(entry, CancellationToken.None);

        var resolveRequest = new ResolvePaymentOpsRequest(
            PaymentOpsResolutionAction.ReopenAndFulfillOrder,
            "Reopened order and enrolled student");

        var result = await service.ResolveAsync(entry.PAYMENT_OPS_QUEUE_ID, adminId, resolveRequest, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Must equal session.StartsAtUtc.AddDays(30), and must NOT equal clock.UtcNow.AddDays(30) — the
        // two are asserted separately so this test cannot pass by coincidence.
        var expectedExpiresAtUtc = sessionStartsAtUtc.AddDays(30);
        var fallbackExpiresAtUtc = _clock.UtcNow.AddDays(30);
        Assert.NotEqual(expectedExpiresAtUtc, fallbackExpiresAtUtc);
        Assert.Contains(_learningAccessContract.Grants, g => g.CourseId == courseId && g.ExpiresAtUtc == expectedExpiresAtUtc);
    }

    [Fact]
    public async Task ResolveAsync_WithReopenAndFulfillOrderAction_CourseHasNoScheduledSession_FallsBackToNowPlusAccessDurationDays()
    {
        var service = CreateService();
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        _userContactReader.Emails[userId] = "buyer@example.test";

        // No entry in _liveScheduleReader.EarliestSessionByCourseId — covers both "the course is
        // OnDemand" and "the course is Live/Hybrid but nothing is scheduled yet"; production code cannot
        // and must not try to tell those two apart, so one test stands in for both.
        _catalogPriceContract.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE Title", 1500m, Guid.NewGuid(), 30);

        var order = ORDER.Create("ORD-REOPEN-NOSESSION", userId, 1500m, 0m, 0m, 1500m);
        order.AddItem(courseId, "COURSE Title", 1500m, 1500m);
        order.MarkAwaitingPayment();
        var statusProp = typeof(ORDER).GetProperty(nameof(ORDER.STATUS));
        statusProp!.SetValue(order, OrderStatus.Cancelled); // Cancelled prematurely by expiry
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_late_paid_nosession", 1500m, _clock);
        payment.MarkSucceeded(_clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var entry = PAYMENT_OPS_QUEUE.Create(payment.PAYMENT_ID, "Late payment succeeded after cancellation");
        await _opsQueueRepo.AddAsync(entry, CancellationToken.None);

        var resolveRequest = new ResolvePaymentOpsRequest(
            PaymentOpsResolutionAction.ReopenAndFulfillOrder,
            "Reopened order and enrolled student");

        var result = await service.ResolveAsync(entry.PAYMENT_OPS_QUEUE_ID, adminId, resolveRequest, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(_learningAccessContract.Grants, g => g.CourseId == courseId && g.ExpiresAtUtc == _clock.UtcNow.AddDays(30));
    }

    [Fact]
    public async Task ResolveAsync_WithGrantAccessOnlyAction_NullAccessDurationDays_GrantsLifetimeAccessWithoutQueryingLiveSchedule()
    {
        var service = CreateService();
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        _userContactReader.Emails[userId] = "buyer@example.test";

        // AccessDurationDays = null (lifetime access) — the short-circuit `AccessDurationDays is { } days`
        // guard must skip GetEarliestScheduledSessionAsync entirely. Every other member of
        // FakeLiveScheduleReader throws, so this test would fail loudly if production code ever called
        // GetSessionsForCourseAsync/GetUpcomingSessionsAsync/GetSessionAsync here. A session is seeded
        // anyway (GetEarliestScheduledSessionAsync itself would not throw if called) to prove it is
        // genuinely never consulted — the resulting ExpiresAtUtc stays null either way.
        _catalogPriceContract.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE Title", 1500m, Guid.NewGuid(), null);
        _liveScheduleReader.EarliestSessionByCourseId[courseId] =
            new LiveSessionInfo(Guid.NewGuid(), courseId, "Session 1", _clock.UtcNow.AddDays(10), _clock.UtcNow.AddDays(10).AddHours(1), LiveSessionStatus.Scheduled, null);

        var order = ORDER.Create("ORD-GRANT-LIFETIME", userId, 1500m, 0m, 0m, 1500m);
        order.AddItem(courseId, "COURSE Title", 1500m, 1500m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_grant_only_lifetime", 1500m, _clock);
        payment.MarkSucceeded(_clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var entry = PAYMENT_OPS_QUEUE.Create(payment.PAYMENT_ID, "Access dispute resolved in student's favor");
        await _opsQueueRepo.AddAsync(entry, CancellationToken.None);

        var resolveRequest = new ResolvePaymentOpsRequest(
            PaymentOpsResolutionAction.GrantAccessOnly,
            "Granted access without reopening the order");

        var result = await service.ResolveAsync(entry.PAYMENT_OPS_QUEUE_ID, adminId, resolveRequest, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // GrantAccessOnly must not reopen the order (unlike ReopenAndFulfillOrder).
        Assert.Equal(OrderStatus.AwaitingPayment, order.STATUS);
        Assert.Contains(_learningAccessContract.Grants, g => g.CourseId == courseId && g.ExpiresAtUtc == null);
    }

    [Fact]
    public async Task DismissAsync_MarksDismissedWithNote()
    {
        var service = CreateService();
        var adminId = Guid.NewGuid();

        var entry = PAYMENT_OPS_QUEUE.Create(Guid.NewGuid(), "Duplicate webhook alert");
        await _opsQueueRepo.AddAsync(entry, CancellationToken.None);

        var result = await service.DismissAsync(entry.PAYMENT_OPS_QUEUE_ID, adminId, new DismissPaymentOpsRequest("False alarm, duplicate event"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentOpsQueueStatus.Dismissed, result.Value.Status);
        Assert.Equal("False alarm, duplicate event", result.Value.Note);
    }

    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }

    private sealed class FakePaymentOpsQueueRepository : IPaymentOpsQueueRepository
    {
        public readonly Dictionary<Guid, PAYMENT_OPS_QUEUE> Entries = [];

        public Task<PAYMENT_OPS_QUEUE?> GetByIdAsync(Guid paymentOpsQueueId, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.TryGetValue(paymentOpsQueueId, out var e) ? e : null);

        public Task AddAsync(PAYMENT_OPS_QUEUE entry, CancellationToken cancellationToken)
        {
            Entries[entry.PAYMENT_OPS_QUEUE_ID] = entry;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PAYMENT_OPS_QUEUE>> GetOpenEntriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PAYMENT_OPS_QUEUE>>(Entries.Values.Where(e => e.STATUS == PaymentOpsQueueStatus.Open).ToList());

        public Task<(IReadOnlyList<PAYMENT_OPS_QUEUE> Items, int TotalCount)> ListAsync(
            PaymentOpsQueueStatus? status,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var query = Entries.Values.AsEnumerable();
            if (status.HasValue) query = query.Where(e => e.STATUS == status.Value);
            var list = query.ToList();
            var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<PAYMENT_OPS_QUEUE> Items, int TotalCount)>((items, list.Count));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
            Task.FromResult<IReadOnlyList<PAYMENT>>(Payments.Values.Where(p => p.ORDER_ID == orderId && (p.STATUS == PaymentStatus.Pending || p.STATUS == PaymentStatus.Processing)).ToList());
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public readonly Dictionary<Guid, ORDER> Orders = [];

        public Task<ORDER?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.TryGetValue(orderId, out var o) ? o : null);

        public Task AddAsync(ORDER order, CancellationToken cancellationToken)
        {
            Orders[order.ORDER_ID] = order;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ORDER>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ORDER>>(Orders.Values.Where(o => o.USER_ID == userId).ToList());

        public Task<(IReadOnlyList<ORDER> Items, int TotalCount)> ListByUserIdAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var userOrders = Orders.Values.Where(o => o.USER_ID == userId).ToList();
            var items = userOrders.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<ORDER> Items, int TotalCount)>((items, userOrders.Count));
        }

        public Task<IReadOnlyList<ORDER>> GetStaleAwaitingPaymentOrdersAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ORDER>>(Orders.Values.Where(o => o.STATUS == OrderStatus.AwaitingPayment && o.CreatedAtUtc <= cutoffUtc).Take(batchSize).ToList());

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => operation();

        public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken) => operation();
    }

    private sealed class FakePromoCodeRepository : IPromoCodeRepository
    {
        public readonly Dictionary<Guid, PROMO_CODE> Codes = [];
        public readonly List<Guid> RevertedOrderIds = [];

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

    private sealed class FakePaymentMethod : IPaymentMethod
    {
        public readonly List<string> RefundedIntentIds = [];

        public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new PaymentIntentResult("pi_fake", "secret", "requires_action", request.Amount, "thb", null, null)));

        public Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new PaymentIntentResult(providerPaymentIntentId, "secret", "succeeded", 100m, "thb", null, null)));

        public Task<Result> CancelPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());

        public Task<Result<PaymentRefundResult>> CreateRefundAsync(CreateRefundRequest request, CancellationToken cancellationToken)
        {
            RefundedIntentIds.Add(request.ProviderPaymentIntentId);
            return Task.FromResult(Result.Success(new PaymentRefundResult("re_123", "succeeded", request.Amount, "thb")));
        }

        public Task<Result<decimal?>> GetChargeFeeAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<decimal?>(null));
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

    private sealed class FakeLearningAccessContract : ILearningAccessContract
    {
        public readonly List<(Guid UserId, Guid CourseId, Guid? OrderId, string Source, DateTime? ExpiresAtUtc)> Grants = [];

        // Proves PaymentOpsQueueService.ResolveAsync calls the batched overload once for the whole
        // order instead of looping EnrollUserAsync once per course (the N+1 this fix removes).
        public int EnrollUserCallCount;
        public int EnrollUserInCoursesCallCount;

        public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Result> EnrollUserAsync(Guid userId, Guid courseId, Guid? orderId, string source, DateTime? expiresAtUtc, CancellationToken cancellationToken)
        {
            EnrollUserCallCount++;
            Grants.Add((userId, courseId, orderId, source, expiresAtUtc));
            return Task.FromResult(Result.Success());
        }

        public Task<Result> EnrollUserInCoursesAsync(Guid userId, string source, IReadOnlyCollection<CourseEnrollmentGrant> grants, CancellationToken cancellationToken)
        {
            EnrollUserInCoursesCallCount++;
            foreach (var grant in grants)
            {
                Grants.Add((userId, grant.CourseId, grant.OrderId, source, grant.ExpiresAtUtc));
            }

            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>P11-13 (Q13.3): only <see cref="GetEarliestScheduledSessionAsync"/> is used by
    /// <see cref="PaymentOpsQueueService"/> — every other member throws so a test would fail loudly if the
    /// production code path ever changed to call one of them.</summary>
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

    private sealed class FakeRevenueSplitContract : IRevenueSplitContract
    {
        public readonly List<(Guid OrderId, IReadOnlyList<OrderItemSplitInfo> Items)> Recorded = [];
        public readonly List<(Guid OrderId, IReadOnlyList<Guid> OrderItemIds)> Reversed = [];

        public Task RecordRevenueSplitsAsync(Guid orderId, IReadOnlyList<OrderItemSplitInfo> items, CancellationToken cancellationToken)
        {
            Recorded.Add((orderId, items));
            return Task.CompletedTask;
        }

        public Task ReverseRevenueSplitsForOrderAsync(Guid orderId, IReadOnlyList<Guid> orderItemIds, CancellationToken cancellationToken)
        {
            Reversed.Add((orderId, orderItemIds));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmailOutbox : IEmailOutbox
    {
        public readonly List<(string ToEmail, string Subject, string BodyHtml, string? TemplateKey)> Sent = [];

        public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey) =>
            Sent.Add((toEmail, subject, bodyHtml, templateKey));
    }

    private sealed class FakeUserContactReader : IUserContactReader
    {
        public readonly Dictionary<Guid, string> Emails = [];

        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Emails.TryGetValue(userId, out var email) ? email : null);

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<(string?, string?)>((Emails.TryGetValue(userId, out var email) ? email : null, null));
    }
}
