using Siri.Integrations.Payment;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Commerce;

public class PaymentServiceTests
{
    private readonly FakePaymentRepository _paymentRepo = new();
    private readonly FakeOrderRepository _orderRepo = new();
    private readonly FakePaymentMethod _paymentMethod = new();
    private readonly FakeClock _clock = new(new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc));

    private PaymentService CreateService() => new(_paymentRepo, _orderRepo, _paymentMethod, _clock);

    [Fact]
    public async Task GetByIdAsync_PaymentNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        var result = await service.GetByIdAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetByIdAsync_OtherUserOrder_ReturnsNotFound()
    {
        var callerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var order = ORDER.Create("ORD-01", otherUserId, 1000m, 0m, 0m, 1000m);
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_test_123", 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var service = CreateService();
        var result = await service.GetByIdAsync(callerUserId, payment.PAYMENT_ID, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetByIdAsync_OwnerMatches_ReturnsSuccess()
    {
        var userId = Guid.NewGuid();

        var order = ORDER.Create("ORD-01", userId, 1000m, 0m, 0m, 1000m);
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_test_123", 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var service = CreateService();
        var result = await service.GetByIdAsync(userId, payment.PAYMENT_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(payment.PAYMENT_ID, result.Value.Id);
        Assert.Equal(1000m, result.Value.Amount);
    }

    [Fact]
    public async Task CreateAsync_OrderNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        var command = new CreatePaymentCommand(Guid.NewGuid(), PaymentMethod.PromptPay);

        var result = await service.CreateAsync(Guid.NewGuid(), command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task CreateAsync_OtherUserOrder_ReturnsNotFound()
    {
        var callerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var order = ORDER.Create("ORD-01", otherUserId, 1000m, 0m, 0m, 1000m);
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var service = CreateService();
        var command = new CreatePaymentCommand(order.ORDER_ID, PaymentMethod.PromptPay);

        var result = await service.CreateAsync(callerUserId, command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task CreateAsync_ValidPendingOrder_CreatesPaymentAndUpdatesOrderStatus()
    {
        var userId = Guid.NewGuid();
        var order = ORDER.Create("ORD-01", userId, 1500m, 0m, 0m, 1500m);
        await _orderRepo.AddAsync(order, CancellationToken.None);

        _paymentMethod.ResultToReturn = Result.Success(new PaymentIntentResult(
            "pi_stripe_abc",
            "pi_stripe_abc_secret",
            "requires_action",
            1500m,
            "thb",
            "https://stripe.com/qr/sample",
            "000201010212..."));

        var service = CreateService();
        var command = new CreatePaymentCommand(order.ORDER_ID, PaymentMethod.PromptPay);

        var result = await service.CreateAsync(userId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.AwaitingPayment, order.STATUS);
        Assert.Equal(order.ORDER_ID, result.Value.OrderId);
        Assert.Equal(1500m, result.Value.Amount);
        Assert.Equal("pi_stripe_abc_secret", result.Value.ClientSecret);
        Assert.Equal("https://stripe.com/qr/sample", result.Value.QrCodeUrl);

        var savedPayment = await _paymentRepo.GetByIdAsync(result.Value.Id, CancellationToken.None);
        Assert.NotNull(savedPayment);
        Assert.Equal("pi_stripe_abc", savedPayment.PROVIDER_PAYMENT_INTENT_ID);
        Assert.Equal(PaymentStatus.Pending, savedPayment.STATUS);
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly List<PAYMENT> _payments = [];

        public Task<PAYMENT?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(_payments.FirstOrDefault(p => p.PAYMENT_ID == paymentId));

        public Task<PAYMENT?> GetByProviderPaymentIntentIdAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(_payments.FirstOrDefault(p => p.PROVIDER_PAYMENT_INTENT_ID == providerPaymentIntentId));

        public Task AddAsync(PAYMENT payment, CancellationToken cancellationToken)
        {
            _payments.Add(payment);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PAYMENT>> GetPendingByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PAYMENT>>(_payments.Where(p => p.ORDER_ID == orderId && (p.STATUS == PaymentStatus.Pending || p.STATUS == PaymentStatus.Processing)).ToList());
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        private readonly List<ORDER> _orders = [];

        public Task<ORDER?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(_orders.FirstOrDefault(o => o.ORDER_ID == orderId));

        public Task AddAsync(ORDER order, CancellationToken cancellationToken)
        {
            _orders.Add(order);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ORDER>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ORDER>>(_orders.Where(o => o.USER_ID == userId).ToList());

        public Task<(IReadOnlyList<ORDER> Items, int TotalCount)> ListByUserIdAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var userOrders = _orders.Where(o => o.USER_ID == userId).ToList();
            var items = userOrders.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<ORDER> Items, int TotalCount)>((items, userOrders.Count));
        }

        public Task<IReadOnlyList<ORDER>> GetStaleAwaitingPaymentOrdersAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
        {
            var result = _orders
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

    private sealed class FakePaymentMethod : IPaymentMethod
    {
        public Result<PaymentIntentResult> ResultToReturn { get; set; } =
            Result.Success(new PaymentIntentResult("pi_default", "secret_default", "requires_action", 100m, "thb", null, null));

        public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(ResultToReturn);

        public Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(ResultToReturn);

        public Task<Result> CancelPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());

        public Task<Result<PaymentRefundResult>> CreateRefundAsync(CreateRefundRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new PaymentRefundResult("re_test", "succeeded", request.Amount, "thb")));
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
