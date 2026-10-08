using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Live.Contracts;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class RefundServiceTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeRefundRepository : IRefundRepository
    {
        public readonly Dictionary<Guid, REFUND> Refunds = [];

        public Task<REFUND?> GetByIdAsync(Guid refundId, CancellationToken cancellationToken) =>
            Task.FromResult(Refunds.TryGetValue(refundId, out var refund) ? refund : null);

        public Task AddAsync(REFUND refund, CancellationToken cancellationToken)
        {
            Refunds[refund.REFUND_ID] = refund;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<REFUND>> GetPendingAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<REFUND> list = Refunds.Values.Where(r => r.STATUS == RefundStatus.Requested).ToList();
            return Task.FromResult(list);
        }

        public Task<(IReadOnlyList<REFUND> Items, int TotalCount)> ListPendingPagedAsync(int page, int pageSize, CancellationToken cancellationToken)
        {
            var pending = Refunds.Values.Where(r => r.STATUS == RefundStatus.Requested).ToList();
            var items = pending.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<REFUND>)items, pending.Count));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        public readonly Dictionary<Guid, PAYMENT> Payments = [];

        public Task<PAYMENT?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(Payments.TryGetValue(paymentId, out var payment) ? payment : null);

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
            Task.FromResult(Orders.TryGetValue(orderId, out var order) ? order : null);

        public Task<IReadOnlyList<ORDER>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken)
        {
            IReadOnlyList<ORDER> list = Orders.Values.Where(o => o.USER_ID == userId && o.STATUS == OrderStatus.Pending).ToList();
            return Task.FromResult(list);
        }

        public Task<(IReadOnlyList<ORDER> Items, int TotalCount)> ListByUserIdAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var userOrders = Orders.Values.Where(o => o.USER_ID == userId).ToList();
            var items = userOrders.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<ORDER> Items, int TotalCount)>((items, userOrders.Count));
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

        public Task AddAsync(ORDER order, CancellationToken cancellationToken)
        {
            Orders[order.ORDER_ID] = order;
            return Task.CompletedTask;
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

    [Fact]
    public async Task RequestAsync_WhenValid_CreatesRefundRequest()
    {
        var refundRepo = new FakeRefundRepository();
        var paymentRepo = new FakePaymentRepository();
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new RefundService(refundRepo, paymentRepo, orderRepo, promoRepo, clock);

        var userId = Guid.NewGuid();
        var order = ORDER.Create("ORD-TEST-001", userId, 1000m, 0m, 0m, 1000m);
        orderRepo.Orders[order.ORDER_ID] = order;

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_test_123", 1000m, clock);
        payment.MarkSucceeded(clock);
        paymentRepo.Payments[payment.PAYMENT_ID] = payment;

        var command = new RequestRefundCommand(payment.PAYMENT_ID, 1000m, "COURSE not as expected");
        var result = await service.RequestAsync(userId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RefundStatus.Requested, result.Value.Status);
        Assert.Equal(1000m, result.Value.Amount);
        Assert.Single(refundRepo.Refunds);
    }

    [Fact]
    public async Task RequestAsync_WhenNotOwner_ReturnsForbidden()
    {
        var refundRepo = new FakeRefundRepository();
        var paymentRepo = new FakePaymentRepository();
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new RefundService(refundRepo, paymentRepo, orderRepo, promoRepo, clock);

        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var order = ORDER.Create("ORD-TEST-002", ownerId, 1000m, 0m, 0m, 1000m);
        orderRepo.Orders[order.ORDER_ID] = order;

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_test_123", 1000m, clock);
        payment.MarkSucceeded(clock);
        paymentRepo.Payments[payment.PAYMENT_ID] = payment;

        var command = new RequestRefundCommand(payment.PAYMENT_ID, 1000m, "Not owner");
        var result = await service.RequestAsync(callerId, command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task ApproveAsync_WhenRequested_MarksApproved()
    {
        var refundRepo = new FakeRefundRepository();
        var paymentRepo = new FakePaymentRepository();
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new RefundService(refundRepo, paymentRepo, orderRepo, promoRepo, clock);

        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var refund = REFUND.Request(Guid.NewGuid(), 500m, "Reason", userId, clock);
        refundRepo.Refunds[refund.REFUND_ID] = refund;

        var result = await service.ApproveAsync(adminId, refund.REFUND_ID, new ApproveRefundCommand("Approved by admin"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RefundStatus.Approved, result.Value.Status);
        Assert.Equal("Approved by admin", result.Value.DecisionNote);
    }

    [Fact]
    public async Task ApproveAsync_WithPromoCode_RevertsPromoRedemption()
    {
        var refundRepo = new FakeRefundRepository();
        var paymentRepo = new FakePaymentRepository();
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new RefundService(refundRepo, paymentRepo, orderRepo, promoRepo, clock);

        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var promoId = Guid.NewGuid();
        var order = ORDER.Create("ORD-PROMO-001", userId, 1000m, 200m, 56m, 800m, promoId);
        orderRepo.Orders[order.ORDER_ID] = order;

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_test_promo", 800m, clock);
        payment.MarkSucceeded(clock);
        paymentRepo.Payments[payment.PAYMENT_ID] = payment;

        promoRepo.Redemptions.Add(PROMO_REDEMPTION.Create(promoId, order.ORDER_ID, userId, clock));
        Assert.Single(promoRepo.Redemptions);

        var refund = REFUND.Request(payment.PAYMENT_ID, 800m, "Refund course", userId, clock);
        refundRepo.Refunds[refund.REFUND_ID] = refund;

        var result = await service.ApproveAsync(adminId, refund.REFUND_ID, new ApproveRefundCommand("Refunded"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RefundStatus.Approved, result.Value.Status);
        Assert.Empty(promoRepo.Redemptions);
    }

    [Fact]
    public async Task RejectAsync_WhenRequested_MarksRejected()
    {
        var refundRepo = new FakeRefundRepository();
        var paymentRepo = new FakePaymentRepository();
        var orderRepo = new FakeOrderRepository();
        var promoRepo = new FakePromoCodeRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new RefundService(refundRepo, paymentRepo, orderRepo, promoRepo, clock);

        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var refund = REFUND.Request(Guid.NewGuid(), 500m, "Reason", userId, clock);
        refundRepo.Refunds[refund.REFUND_ID] = refund;

        var result = await service.RejectAsync(adminId, refund.REFUND_ID, new RejectRefundCommand("Policy violated"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RefundStatus.Rejected, result.Value.Status);
        Assert.Equal("Policy violated", result.Value.DecisionNote);
    }

    // ======================================================================================================
    // P11-12 (docs/contracts/P11-12-refund-hard-block-after-live-join.md section 5): the live-attendance hard block.
    // ======================================================================================================

    private sealed class FakeLiveAttendanceReader(params Guid[] attended) : ILiveAttendanceReader
    {
        public readonly List<(Guid UserId, Guid[] CourseIds)> Calls = [];

        public Task<IReadOnlySet<Guid>> GetCourseIdsAttendedAsync(Guid userId, IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken)
        {
            Calls.Add((userId, courseIds.ToArray()));
            IReadOnlySet<Guid> result = courseIds.Where(id => attended.Contains(id)).ToHashSet();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyDictionary<Guid, LiveSessionStats>> GetSessionStatsAsync(
            IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("RefundService must never ask Live for session statistics.");
    }

    /// <summary>A buyer, one order with the given lines and one succeeded payment — the shared arrangement of the live-attendance tests.</summary>
    private sealed class LiveScene
    {
        public readonly FakeRefundRepository Refunds = new();
        public readonly FakePaymentRepository Payments = new();
        public readonly FakeOrderRepository Orders = new();
        public readonly FakePromoCodeRepository Promos = new();
        public readonly FakeClock Clock = new(DateTime.UtcNow);
        public readonly Guid UserId = Guid.NewGuid();

        public LiveScene(decimal paymentAmount, params (Guid? CourseId, string Title, decimal LineTotal)[] lines)
        {
            Order = ORDER.Create($"ORD-{Guid.NewGuid():N}"[..20], UserId, lines.Sum(l => l.LineTotal), 0m, 0m, paymentAmount);
            foreach (var (courseId, title, lineTotal) in lines)
            {
                AddLine(courseId, title, lineTotal, lineTotal);
            }

            Orders.Orders[Order.ORDER_ID] = Order;

            Payment = PAYMENT.Create(Order.ORDER_ID, PaymentMethod.PromptPay, $"pi_{Guid.NewGuid():N}", paymentAmount, Clock);
            Payment.MarkSucceeded(Clock);
            Payments.Payments[Payment.PAYMENT_ID] = Payment;
        }

        public ORDER Order { get; }

        public PAYMENT Payment { get; }

        /// <summary>A line whose <paramref name="unitPrice"/> (list price) differs from its <paramref name="lineTotal"/> (what was actually charged for it).</summary>
        public void AddLine(Guid? courseId, string title, decimal unitPrice, decimal lineTotal) =>
            Order.AddItem(courseId, title, unitPrice, lineTotal);

        public RefundService ServiceWith(ILiveAttendanceReader? reader) =>
            new(Refunds, Payments, Orders, Promos, Clock, revenueSplitContract: null, liveAttendance: reader);

        public Task<Result<RefundResponse>> RequestAsync(RefundService service, decimal amount, Guid? asUser = null) =>
            service.RequestAsync(asUser ?? UserId, new RequestRefundCommand(Payment.PAYMENT_ID, amount, "ไม่ตรงกับที่คาดหวัง"), CancellationToken.None);
    }

    private static void AssertLiveAttended(Result<RefundResponse> result, decimal maxRefundable, decimal blockedAmount, params string[] blockedTitles)
    {
        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal("refund.live_attended", result.Error.Reason);
        Assert.Equal(RefundReasons.LiveAttended, result.Error.Reason);
        var extensions = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result.Error.Extensions);
        Assert.Equal(["blockedAmount", "blockedCourseTitles", "maxRefundableAmount"], extensions.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(maxRefundable, Assert.IsType<decimal>(extensions["maxRefundableAmount"]));
        Assert.Equal(blockedAmount, Assert.IsType<decimal>(extensions["blockedAmount"]));
        Assert.Equal(blockedTitles, Assert.IsAssignableFrom<IReadOnlyList<string>>(extensions["blockedCourseTitles"]));
    }

    [Fact]
    public async Task RequestAsync_WithoutALiveAttendanceReader_BehavesExactlyAsBefore_EvenForAFullRefund()
    {
        var scene = new LiveScene(1000m, (Guid.NewGuid(), "คอร์สสด", 1000m));

        var result = await scene.RequestAsync(scene.ServiceWith(null), 1000m);

        Assert.True(result.IsSuccess);
        Assert.Single(scene.Refunds.Refunds);
    }

    [Fact]
    public async Task RequestAsync_BuyerNeverJoinedALiveRoom_FullRefundIsAllowed_AndLiveIsAskedOnceWithTheOrderOwnersId()
    {
        var courseA = Guid.NewGuid();
        var courseB = Guid.NewGuid();
        var scene = new LiveScene(1500m, (courseA, "คอร์ส A", 1000m), (courseB, "คอร์ส B", 500m));
        var reader = new FakeLiveAttendanceReader(); // nobody attended anything

        var result = await scene.RequestAsync(scene.ServiceWith(reader), 1500m);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(reader.Calls); // one batch call, never one per course
        Assert.Equal(scene.UserId, call.UserId);
        Assert.Equal(new[] { courseA, courseB }.Order().ToArray(), call.CourseIds.Order().ToArray());
        Assert.Single(scene.Refunds.Refunds);
    }

    [Fact]
    public async Task RequestAsync_SingleCourseOrderWhereTheBuyerAttended_FullAmountIsRefused_AndNothingIsCreated()
    {
        var course = Guid.NewGuid();
        var scene = new LiveScene(1000m, (course, "คอร์สสดพื้นฐาน", 1000m));

        var result = await scene.RequestAsync(scene.ServiceWith(new FakeLiveAttendanceReader(course)), 1000m);

        AssertLiveAttended(result, maxRefundable: 0m, blockedAmount: 1000m, "คอร์สสดพื้นฐาน");
        Assert.Empty(scene.Refunds.Refunds);
    }

    [Fact]
    public async Task RequestAsync_SingleCourseOrderWhereTheBuyerAttended_EvenOneSatangIsRefused()
    {
        var course = Guid.NewGuid();
        var scene = new LiveScene(1000m, (course, "คอร์สสดพื้นฐาน", 1000m));

        var result = await scene.RequestAsync(scene.ServiceWith(new FakeLiveAttendanceReader(course)), 0.01m);

        AssertLiveAttended(result, maxRefundable: 0m, blockedAmount: 1000m, "คอร์สสดพื้นฐาน");
        Assert.Empty(scene.Refunds.Refunds);
    }

    [Fact]
    public async Task RequestAsync_TwoCourseOrderWhereOnlyOneWasAttended_TheOtherCoursesShareCanStillBeRefunded_ToTheSatang()
    {
        var live = Guid.NewGuid();
        var recorded = Guid.NewGuid();
        var scene = new LiveScene(1500m, (live, "คอร์สสด", 1000m), (recorded, "คอร์สบันทึก", 500m));
        var service = scene.ServiceWith(new FakeLiveAttendanceReader(live));

        var allowed = await scene.RequestAsync(service, 500m);
        var oneSatangOver = await scene.RequestAsync(service, 500.01m);
        var fullAmount = await scene.RequestAsync(service, 1500m);

        Assert.True(allowed.IsSuccess);
        Assert.Equal(500m, allowed.Value.Amount);
        AssertLiveAttended(oneSatangOver, maxRefundable: 500.00m, blockedAmount: 1000.00m, "คอร์สสด");
        AssertLiveAttended(fullAmount, maxRefundable: 500.00m, blockedAmount: 1000.00m, "คอร์สสด");
        Assert.Single(scene.Refunds.Refunds); // only the allowed request produced a refund row
    }

    [Fact]
    public async Task RequestAsync_BuyerAttendedEveryCourseInTheOrder_NothingCanBeRefunded_AllTitlesAreListedInPurchaseOrder()
    {
        var courseA = Guid.NewGuid();
        var courseB = Guid.NewGuid();
        var scene = new LiveScene(1500m, (courseA, "คอร์ส A", 1000m), (courseB, "คอร์ส B", 500m));

        var result = await scene.RequestAsync(scene.ServiceWith(new FakeLiveAttendanceReader(courseA, courseB)), 0.01m);

        AssertLiveAttended(result, maxRefundable: 0m, blockedAmount: 1500m, "คอร์ส A", "คอร์ส B");
    }

    [Fact]
    public async Task RequestAsync_CeilingUsesTheLineTotalsAfterDiscount_NotTheListPrices()
    {
        // Paid 800 for lines of 600 (attended) and 200: the attended share is 600 of 800. List prices (1000/500) would have given 533.33.
        var live = Guid.NewGuid();
        var scene = new LiveScene(800m);
        scene.AddLine(live, "คอร์สสด", unitPrice: 1000m, lineTotal: 600m);
        scene.AddLine(Guid.NewGuid(), "คอร์สบันทึก", unitPrice: 500m, lineTotal: 200m);
        var service = scene.ServiceWith(new FakeLiveAttendanceReader(live));

        var allowed = await scene.RequestAsync(service, 200m);
        var over = await scene.RequestAsync(service, 200.01m);

        Assert.True(allowed.IsSuccess);
        AssertLiveAttended(over, maxRefundable: 200m, blockedAmount: 600m, "คอร์สสด");
    }

    [Fact]
    public async Task RequestAsync_PaymentAmountDiffersFromTheLineSum_TheBlockedShareIsProportionalAndRoundedAwayFromZero()
    {
        // 100.00 paid against lines of 10 (attended) and 20: blocked = 100 * 10 / 30 = 33.333... -> 33.33, so at most 66.67 comes back.
        var live = Guid.NewGuid();
        var scene = new LiveScene(100m, (live, "คอร์สสด", 10m), (Guid.NewGuid(), "คอร์สบันทึก", 20m));
        var service = scene.ServiceWith(new FakeLiveAttendanceReader(live));

        var allowed = await scene.RequestAsync(service, 66.67m);
        var over = await scene.RequestAsync(service, 66.68m);

        Assert.True(allowed.IsSuccess);
        AssertLiveAttended(over, maxRefundable: 66.67m, blockedAmount: 33.33m, "คอร์สสด");
    }

    [Fact]
    public async Task RequestAsync_HalfSatangShareRoundsAwayFromZero_NotToEven()
    {
        // 1.00 * 1 / 8 = 0.125 exactly: away from zero gives 0.13 (banker rounding would give 0.12 and a ceiling of 0.88).
        var live = Guid.NewGuid();
        var scene = new LiveScene(1m, (live, "คอร์สสด", 1m), (Guid.NewGuid(), "คอร์สบันทึก", 7m));
        var service = scene.ServiceWith(new FakeLiveAttendanceReader(live));

        var allowed = await scene.RequestAsync(service, 0.87m);
        var over = await scene.RequestAsync(service, 0.88m);

        Assert.True(allowed.IsSuccess);
        AssertLiveAttended(over, maxRefundable: 0.87m, blockedAmount: 0.13m, "คอร์สสด");
    }

    [Fact]
    public async Task RequestAsync_LineWithoutACourse_IsNeverBlockedByItself_ButStillCountsInTheTotal()
    {
        // A bundle line (no single course) of 300 next to an attended course of 700: the course's 700 of 1000 is blocked, the bundle's 300 is refundable.
        var live = Guid.NewGuid();
        var reader = new FakeLiveAttendanceReader(live);
        var scene = new LiveScene(1000m, (null, "ชุดคอร์ส", 300m), (live, "คอร์สสด", 700m));
        var service = scene.ServiceWith(reader);

        var allowed = await scene.RequestAsync(service, 300m);
        var over = await scene.RequestAsync(service, 300.01m);

        Assert.True(allowed.IsSuccess);
        AssertLiveAttended(over, maxRefundable: 300m, blockedAmount: 700m, "คอร์สสด");
        Assert.All(reader.Calls, call => Assert.Equal([live], call.CourseIds)); // the null course id never reaches Live
    }

    [Fact]
    public async Task RequestAsync_OrderWithOnlyCourselessLines_NeverAsksLive_FullRefundAllowed()
    {
        var reader = new FakeLiveAttendanceReader();
        var scene = new LiveScene(300m, (null, "ชุดคอร์ส", 300m));

        var result = await scene.RequestAsync(scene.ServiceWith(reader), 300m);

        Assert.True(result.IsSuccess);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task RequestAsync_LineTotalsSumToZeroButTheBuyerAttended_NothingIsRefundable()
    {
        // Degenerate: the payment is positive but every line is zero (no basis to split). The whole payment counts as blocked.
        var live = Guid.NewGuid();
        var scene = new LiveScene(100m, (live, "คอร์สสด (ฟรี)", 0m));

        var result = await scene.RequestAsync(scene.ServiceWith(new FakeLiveAttendanceReader(live)), 0.01m);

        AssertLiveAttended(result, maxRefundable: 0m, blockedAmount: 100m, "คอร์สสด (ฟรี)");
    }

    [Fact]
    public async Task RequestAsync_SameCourseOnTwoLines_ListsItsTitleOnce()
    {
        var live = Guid.NewGuid();
        var scene = new LiveScene(600m, (live, "คอร์สสด", 300m), (live, "คอร์สสด", 300m));

        var result = await scene.RequestAsync(scene.ServiceWith(new FakeLiveAttendanceReader(live)), 600m);

        AssertLiveAttended(result, maxRefundable: 0m, blockedAmount: 600m, "คอร์สสด");
    }

    [Fact]
    public async Task RequestAsync_NotTheOrdersOwner_IsForbiddenBeforeAnyLiveLookup()
    {
        var live = Guid.NewGuid();
        var reader = new FakeLiveAttendanceReader(live);
        var scene = new LiveScene(1000m, (live, "คอร์สสด", 1000m));

        var result = await scene.RequestAsync(scene.ServiceWith(reader), 1000m, asUser: Guid.NewGuid());

        Assert.Equal("forbidden", result.Error.Code);
        Assert.Null(result.Error.Reason);
        Assert.Empty(reader.Calls); // the 403 is decided first, and the lookup is always by the order's owner — never by whoever is asking
    }

    [Fact]
    public async Task RequestAsync_PaymentAlreadyRefunded_IsRejectedAsNotRefundable_BeforeTheLiveRule()
    {
        var live = Guid.NewGuid();
        var reader = new FakeLiveAttendanceReader(live);
        var scene = new LiveScene(1000m, (live, "คอร์สสด", 1000m));
        scene.Payment.MarkRefunded(scene.Clock);

        var result = await scene.RequestAsync(scene.ServiceWith(reader), 0.01m);

        Assert.Equal("validation", result.Error.Code); // the original rule: only a succeeded payment can be refunded
        Assert.Null(result.Error.Reason);
        Assert.Empty(reader.Calls);
        Assert.Empty(scene.Refunds.Refunds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000.01)]
    public async Task RequestAsync_AmountOutsideTheOriginalBounds_IsStillAValidationError_NotALiveConflict(double amount)
    {
        var live = Guid.NewGuid();
        var reader = new FakeLiveAttendanceReader(live);
        var scene = new LiveScene(1000m, (live, "คอร์สสด", 1000m));

        var result = await scene.RequestAsync(scene.ServiceWith(reader), (decimal)amount);

        Assert.Equal("validation", result.Error.Code);
        Assert.Null(result.Error.Reason);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task ApproveAsync_AfterTheBuyerJoinedALiveRoom_StillApproves_TheAdminsDecisionIsNotReChecked()
    {
        var live = Guid.NewGuid();
        var reader = new FakeLiveAttendanceReader(live);
        var scene = new LiveScene(1000m, (live, "คอร์สสด", 1000m));
        var service = scene.ServiceWith(reader);
        // Filed before the learner ever entered a room (no attendance then), approved by an admin afterwards.
        var refund = REFUND.Request(scene.Payment.PAYMENT_ID, 1000m, "ขอคืนก่อนเข้าเรียน", scene.UserId, scene.Clock);
        scene.Refunds.Refunds[refund.REFUND_ID] = refund;

        var result = await service.ApproveAsync(Guid.NewGuid(), refund.REFUND_ID, new ApproveRefundCommand("อนุมัติ"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RefundStatus.Approved, result.Value.Status);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task RejectAsync_NeverConsultsTheLiveReader()
    {
        var reader = new FakeLiveAttendanceReader(Guid.NewGuid());
        var scene = new LiveScene(1000m, (Guid.NewGuid(), "คอร์ส", 1000m));
        var refund = REFUND.Request(scene.Payment.PAYMENT_ID, 1000m, "เหตุผล", scene.UserId, scene.Clock);
        scene.Refunds.Refunds[refund.REFUND_ID] = refund;

        var result = await scene.ServiceWith(reader).RejectAsync(Guid.NewGuid(), refund.REFUND_ID, new RejectRefundCommand("ไม่เข้าเงื่อนไข"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task DependencyInjection_InjectsTheLiveAttendanceReaderWhenRegistered_AndFallsBackToNoCheckWhenTheHostHasNoLive()
    {
        // The reader is an optional constructor parameter: if the container silently did not inject it, the block would be a no-op in production. Pin both directions.
        var live = Guid.NewGuid();
        var scene = new LiveScene(1000m, (live, "คอร์สสด", 1000m));

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IRefundRepository>(scene.Refunds);
        services.AddSingleton<IPaymentRepository>(scene.Payments);
        services.AddSingleton<IOrderRepository>(scene.Orders);
        services.AddSingleton<IPromoCodeRepository>(scene.Promos);
        services.AddSingleton<IClock>(scene.Clock);
        services.AddScoped<RefundService>();

        await using (var withoutLive = services.BuildServiceProvider(validateScopes: true))
        {
            await using var scope = withoutLive.CreateAsyncScope();
            var result = await scene.RequestAsync(scope.ServiceProvider.GetRequiredService<RefundService>(), 1000m);
            Assert.True(result.IsSuccess); // no Live module in this host: behaves as before
        }

        scene.Refunds.Refunds.Clear();
        services.AddScoped<ILiveAttendanceReader>(_ => new FakeLiveAttendanceReader(live));

        await using (var withLive = services.BuildServiceProvider(validateScopes: true))
        {
            await using var scope = withLive.CreateAsyncScope();
            var result = await scene.RequestAsync(scope.ServiceProvider.GetRequiredService<RefundService>(), 1000m);
            AssertLiveAttended(result, maxRefundable: 0m, blockedAmount: 1000m, "คอร์สสด");
        }
    }

    [Fact]
    public async Task LiveAttendedError_BecomesAnHttp409ProblemWithTheStableReasonAndTheAmounts()
    {
        var live = Guid.NewGuid();
        var scene = new LiveScene(1500m, (live, "คอร์สสด", 1000m), (Guid.NewGuid(), "คอร์สบันทึก", 500m));

        var result = await scene.RequestAsync(scene.ServiceWith(new FakeLiveAttendanceReader(live)), 1500m);

        var http = Assert.IsType<ProblemHttpResult>(result.Error.ToProblemHttpResult(new DefaultHttpContext()));
        Assert.Equal(409, http.StatusCode);
        var body = http.ProblemDetails.Extensions;
        Assert.Equal("conflict", body["errorCode"]);
        Assert.Equal("refund.live_attended", body["reason"]);
        Assert.Equal(500m, body["maxRefundableAmount"]);
        Assert.Equal(1000m, body["blockedAmount"]);
        Assert.Equal(new[] { "คอร์สสด" }, Assert.IsAssignableFrom<IReadOnlyList<string>>(body["blockedCourseTitles"]));
        Assert.True(body.ContainsKey("traceId"));
        // No session id, join time or instructor name in the response — only the buyer's own course titles and two amounts.
        Assert.Equal(["blockedAmount", "blockedCourseTitles", "errorCode", "maxRefundableAmount", "reason", "traceId"], body.Keys.Order(StringComparer.Ordinal).ToArray());
    }
}
