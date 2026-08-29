using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class TaxInvoiceServiceTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeTaxInvoiceRepository : ITaxInvoiceRepository
    {
        public readonly Dictionary<Guid, TAX_INVOICE> Invoices = [];

        public Task<TAX_INVOICE?> GetByIdAsync(Guid taxInvoiceId, CancellationToken cancellationToken) =>
            Task.FromResult(Invoices.TryGetValue(taxInvoiceId, out var inv) ? inv : null);

        public Task<TAX_INVOICE?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Invoices.Values.FirstOrDefault(i => i.ORDER_ID == orderId));

        public Task AddAsync(TAX_INVOICE taxInvoice, CancellationToken cancellationToken)
        {
            Invoices[taxInvoice.TAX_INVOICE_ID] = taxInvoice;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TAX_INVOICE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TAX_INVOICE>>(Invoices.Values.Skip((page - 1) * pageSize).Take(pageSize).ToList());

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Invoices.Count);
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public readonly Dictionary<Guid, ORDER> Orders = [];

        public Task<ORDER?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.TryGetValue(orderId, out var order) ? order : null);

        public Task AddAsync(ORDER order, CancellationToken cancellationToken)
        {
            Orders[order.ORDER_ID] = order;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ORDER>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ORDER>>(Orders.Values.Where(o => o.USER_ID == userId && o.STATUS == OrderStatus.Pending).ToList());

        public Task<(IReadOnlyList<ORDER> Items, int TotalCount)> ListByUserIdAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var userOrders = Orders.Values.Where(o => o.USER_ID == userId).OrderByDescending(o => o.CreatedAtUtc).ToList();
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

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => operation();

        public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken) => operation();
    }

    [Fact]
    public async Task IssueAsync_WhenOrderIsPaid_IssuesInvoice()
    {
        var invoiceRepo = new FakeTaxInvoiceRepository();
        var orderRepo = new FakeOrderRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var order = ORDER.Create("ORD-123", userId, 1000m, 0m, 70m, 1070m);
        order.MarkAwaitingPayment();
        order.MarkPaid(clock);
        await orderRepo.AddAsync(order, CancellationToken.None);

        var service = new TaxInvoiceService(invoiceRepo, orderRepo, clock);
        var command = new IssueTaxInvoiceCommand(order.ORDER_ID, "0105558123456", "บริษัท ตัวอย่าง จำกัด");

        var result = await service.IssueAsync(userId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("บริษัท ตัวอย่าง จำกัด", result.Value.BuyerName);
        Assert.StartsWith("INV-260821-", result.Value.InvoiceNo);
        Assert.Equal(TaxInvoiceStatus.Issued, result.Value.Status);
        Assert.Single(invoiceRepo.Invoices);
    }

    [Fact]
    public async Task IssueAsync_WhenOrderIsNotPaid_ReturnsConflict()
    {
        var invoiceRepo = new FakeTaxInvoiceRepository();
        var orderRepo = new FakeOrderRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var userId = Guid.NewGuid();
        var order = ORDER.Create("ORD-123", userId, 1000m, 0m, 70m, 1070m); // Pending
        await orderRepo.AddAsync(order, CancellationToken.None);

        var service = new TaxInvoiceService(invoiceRepo, orderRepo, clock);
        var command = new IssueTaxInvoiceCommand(order.ORDER_ID, "0105558123456", "บริษัท ตัวอย่าง จำกัด");

        var result = await service.IssueAsync(userId, command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task GetByIdAsync_IDOR_WhenNotOrderOwner_ReturnsNotFound()
    {
        var invoiceRepo = new FakeTaxInvoiceRepository();
        var orderRepo = new FakeOrderRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();

        var order = ORDER.Create("ORD-123", ownerId, 1000m, 0m, 70m, 1070m);
        order.MarkAwaitingPayment();
        order.MarkPaid(clock);
        await orderRepo.AddAsync(order, CancellationToken.None);

        var invoice = TAX_INVOICE.Issue(order.ORDER_ID, "0105558123456", "บริษัท ตัวอย่าง จำกัด", "INV-123", clock);
        invoiceRepo.Invoices[invoice.TAX_INVOICE_ID] = invoice;

        var service = new TaxInvoiceService(invoiceRepo, orderRepo, clock);

        var result = await service.GetByIdAsync(strangerId, invoice.TAX_INVOICE_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetByOrderIdAsync_WhenOwnerRequests_ReturnsTaxInvoice()
    {
        var invoiceRepo = new FakeTaxInvoiceRepository();
        var orderRepo = new FakeOrderRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var order = ORDER.Create("ORD-123", ownerId, 1000m, 0m, 70m, 1070m);
        order.MarkAwaitingPayment();
        order.MarkPaid(clock);
        await orderRepo.AddAsync(order, CancellationToken.None);

        var invoice = TAX_INVOICE.Issue(order.ORDER_ID, "0105558123456", "บริษัท ตัวอย่าง จำกัด", "INV-123", clock);
        invoiceRepo.Invoices[invoice.TAX_INVOICE_ID] = invoice;

        var service = new TaxInvoiceService(invoiceRepo, orderRepo, clock);
        var result = await service.GetByOrderIdAsync(ownerId, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("INV-123", result.Value.InvoiceNo);
        Assert.Equal("บริษัท ตัวอย่าง จำกัด", result.Value.BuyerName);
    }

    [Fact]
    public async Task GetByOrderIdAsync_IDOR_WhenStrangerRequests_ReturnsNotFound()
    {
        var invoiceRepo = new FakeTaxInvoiceRepository();
        var orderRepo = new FakeOrderRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();

        var order = ORDER.Create("ORD-123", ownerId, 1000m, 0m, 70m, 1070m);
        order.MarkAwaitingPayment();
        order.MarkPaid(clock);
        await orderRepo.AddAsync(order, CancellationToken.None);

        var invoice = TAX_INVOICE.Issue(order.ORDER_ID, "0105558123456", "บริษัท ตัวอย่าง จำกัด", "INV-123", clock);
        invoiceRepo.Invoices[invoice.TAX_INVOICE_ID] = invoice;

        var service = new TaxInvoiceService(invoiceRepo, orderRepo, clock);
        var result = await service.GetByOrderIdAsync(strangerId, order.ORDER_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }
}
