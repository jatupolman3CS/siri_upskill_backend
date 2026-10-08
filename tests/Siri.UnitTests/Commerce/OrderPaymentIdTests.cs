using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

/// <summary>
/// <c>OrderResponse.PaymentId</c> (additive, P11-05 hand-off from the frontend): the id of the order's <b>successful</b> payment, which <c>POST /api/commerce/refunds</c> needs but the
/// order DTO did not expose. These tests pin the mapping (detail and list), that the list looks the payments up in <b>one batch</b> for the whole page, that another user's order still
/// answers 404 before any payment is looked up, and the JSON shape.
/// </summary>
public sealed class OrderPaymentIdTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }

    /// <summary>Fails loudly for any dependency the paths under test must not touch.</summary>
    public class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name} is not used by this test.");
    }

    private static T Unused<T>()
        where T : class => DispatchProxy.Create<T, ThrowingProxy>();

    private sealed class FakeOrders : IOrderRepository
    {
        public List<ORDER> Orders { get; } = [];

        public Task<ORDER?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.FirstOrDefault(o => o.ORDER_ID == orderId));

        public Task<(IReadOnlyList<ORDER> Items, int TotalCount)> ListByUserIdAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var mine = Orders.Where(o => o.USER_ID == userId).ToList();
            return Task.FromResult<(IReadOnlyList<ORDER> Items, int TotalCount)>((mine.Skip((page - 1) * pageSize).Take(pageSize).ToList(), mine.Count));
        }

        public Task AddAsync(ORDER order, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ORDER>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ORDER>> GetStaleAwaitingPaymentOrdersAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePayments : IPaymentRepository
    {
        public Dictionary<Guid, Guid> SucceededPaymentByOrder { get; } = [];

        public List<IReadOnlyCollection<Guid>> LookupRequests { get; } = [];

        public Task<PAYMENT?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PAYMENT?> GetByProviderPaymentIntentIdAsync(string providerPaymentIntentId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddAsync(PAYMENT payment, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PAYMENT>> GetPendingByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, Guid>> GetSucceededPaymentIdsByOrderIdsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
        {
            LookupRequests.Add(orderIds.ToArray());
            return Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(
                orderIds.Where(SucceededPaymentByOrder.ContainsKey).ToDictionary(id => id, id => SucceededPaymentByOrder[id]));
        }
    }

    private readonly FakeOrders _orders = new();
    private readonly FakePayments _payments = new();
    private readonly Guid _user = Guid.NewGuid();

    private OrderService Service() => new(
        _orders,
        Unused<IPromoCodeRepository>(),
        Unused<Siri.Modules.Catalog.Contracts.ICatalogPriceContract>(),
        Unused<Siri.Modules.Catalog.Contracts.ILiveScheduleReader>(),
        Unused<Siri.Modules.Learning.Contracts.ILearningAccessContract>(),
        Unused<IPricingEngine>(),
        new FakeClock(new DateTime(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc)),
        _payments,
        Unused<Siri.Integrations.Payment.IPaymentMethod>());

    private ORDER NewOrder(string orderNo, Guid? owner = null)
    {
        var order = ORDER.Create(orderNo, owner ?? _user, 1000m, 0m, 65.42m, 1000m);
        order.AddItem(Guid.NewGuid(), "COURSE", 1000m, 1000m);
        _orders.Orders.Add(order);
        return order;
    }

    [Fact]
    public async Task GetById_OrderWithASucceededPayment_ReportsItsPaymentId()
    {
        var order = NewOrder("ORD-1");
        var paymentId = Guid.NewGuid();
        _payments.SucceededPaymentByOrder[order.ORDER_ID] = paymentId;

        var result = await Service().GetByIdAsync(_user, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(paymentId, result.Value.PaymentId);
        Assert.Equal([order.ORDER_ID], _payments.LookupRequests.Single());
    }

    [Fact]
    public async Task GetById_OrderWithoutASucceededPayment_HasNoPaymentId()
    {
        var order = NewOrder("ORD-2");

        var result = await Service().GetByIdAsync(_user, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PaymentId);
    }

    [Fact]
    public async Task GetById_AnotherUsersOrder_IsNotFound_BeforeAnyPaymentIsLookedUp()
    {
        var theirs = NewOrder("ORD-3", owner: Guid.NewGuid());
        _payments.SucceededPaymentByOrder[theirs.ORDER_ID] = Guid.NewGuid();

        var result = await Service().GetByIdAsync(_user, theirs.ORDER_ID, CancellationToken.None);

        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(_payments.LookupRequests);
    }

    [Fact]
    public async Task List_ReportsThePaymentIdPerOrder_WithOneBatchedLookupForThePage()
    {
        var paid = NewOrder("ORD-A");
        var unpaid = NewOrder("ORD-B");
        var alsoPaid = NewOrder("ORD-C");
        NewOrder("ORD-OTHER", owner: Guid.NewGuid()); // not this user's: never part of the lookup
        var paidPayment = Guid.NewGuid();
        var alsoPaidPayment = Guid.NewGuid();
        _payments.SucceededPaymentByOrder[paid.ORDER_ID] = paidPayment;
        _payments.SucceededPaymentByOrder[alsoPaid.ORDER_ID] = alsoPaidPayment;

        var page = await Service().ListUserOrdersAsync(_user, 1, 20, CancellationToken.None);

        Assert.Equal(3, page.Items.Count);
        Assert.Equal(paidPayment, page.Items.Single(o => o.OrderNo == "ORD-A").PaymentId);
        Assert.Equal(alsoPaidPayment, page.Items.Single(o => o.OrderNo == "ORD-C").PaymentId);
        Assert.Null(page.Items.Single(o => o.OrderNo == "ORD-B").PaymentId);

        // One lookup for the whole page, containing exactly this user's three orders.
        var request = Assert.Single(_payments.LookupRequests);
        Assert.Equal(new[] { paid.ORDER_ID, unpaid.ORDER_ID, alsoPaid.ORDER_ID }.Order(), request.Order());
    }

    [Fact]
    public async Task List_WithNoOrders_IsEmpty()
    {
        var page = await Service().ListUserOrdersAsync(_user, 1, 20, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task TheJsonShape_AddsPaymentIdAtTheEnd_AsAnOptionalGuid()
    {
        var order = NewOrder("ORD-J");
        var paymentId = Guid.NewGuid();
        _payments.SucceededPaymentByOrder[order.ORDER_ID] = paymentId;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };

        var paid = JsonSerializer.Serialize((await Service().GetByIdAsync(_user, order.ORDER_ID, CancellationToken.None)).Value, options);
        _payments.SucceededPaymentByOrder.Clear();
        var unpaid = JsonSerializer.Serialize((await Service().GetByIdAsync(_user, order.ORDER_ID, CancellationToken.None)).Value, options);

        Assert.Contains($"\"paymentId\":\"{paymentId}\"", paid);
        Assert.Contains("\"paymentId\":null", unpaid);
        Assert.Contains("\"items\":", paid);
        Assert.Contains("\"orderNo\":\"ORD-J\"", paid);
    }

    [Fact]
    public void PaymentId_IsAnOptionalLastParameter_SoExistingConstructorCallsStillCompile()
    {
        var response = new OrderResponse(Guid.NewGuid(), "ORD-X", 1m, 0m, 0m, 1m, "THB", OrderStatus.Pending, DateTime.UtcNow, null, []);

        Assert.Null(response.PaymentId);
    }
}
