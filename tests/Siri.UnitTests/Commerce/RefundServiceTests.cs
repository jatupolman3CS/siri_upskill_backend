using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
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

        var command = new RequestRefundCommand(payment.PAYMENT_ID, 1000m, "Course not as expected");
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
}
