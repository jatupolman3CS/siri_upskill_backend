using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class ReceiptPdfGeneratorTests
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
    public void GeneratePdf_ProducesValidPdfBytes()
    {
        var data = new ReceiptPdfData(
            "TAX INVOICE / RECEIPT",
            "INV-20260827-0001",
            "ORD-20260827-9999",
            "บริษัท สิริ จำกัด",
            "0105567012345",
            "customer@example.com",
            DateTime.UtcNow,
            "PromptPay",
            [
                new ReceiptPdfItem("Full-Stack Angular & .NET 10", 1, 1500m, 1500m),
                new ReceiptPdfItem("Enterprise Clean Architecture Masterclass", 1, 2000m, 2000m),
            ],
            3500m,
            500m,
            210m,
            3210m,
            new ReceiptSellerInfo("Test Seller Co., Ltd.", "Head Office", "0105500000001", "1 Test Road, Bangkok", "billing@seller.test", "https://seller.test"));

        var bytes = ReceiptPdfGenerator.GeneratePdf(data);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        // PDF header magic bytes: %PDF-
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public void GeneratePdf_WithoutOptionalSellerFields_StillProducesValidPdf()
    {
        var data = new ReceiptPdfData(
            "OFFICIAL RECEIPT",
            "REC-1",
            "ORD-1",
            "Real Buyer",
            null,
            null,
            DateTime.UtcNow,
            "-",
            [new ReceiptPdfItem("Course", 1, 100m, 100m)],
            100m,
            0m,
            6.54m,
            100m,
            new ReceiptSellerInfo("Test Seller Co., Ltd.", null, "0105500000001", "1 Test Road, Bangkok", null, null));

        var bytes = ReceiptPdfGenerator.GeneratePdf(data);

        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
    }

    [Fact]
    public async Task GetPdfAsync_WhenOwnerRequests_ReturnsPdfBytesAndFileName()
    {
        var invoiceRepo = new FakeTaxInvoiceRepository();
        var orderRepo = new FakeOrderRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var order = ORDER.Create("ORD-555", ownerId, 1000m, 0m, 70m, 1070m);
        order.AddItem(Guid.NewGuid(), "Angular 21 Masterclass", 1000m, 1000m);
        order.MarkAwaitingPayment();
        order.MarkPaid(clock);
        await orderRepo.AddAsync(order, CancellationToken.None);

        var invoice = TAX_INVOICE.Issue(order.ORDER_ID, "0105558123456", "บริษัท ลูกค้า จำกัด", "INV-555", clock);
        invoiceRepo.Invoices[invoice.TAX_INVOICE_ID] = invoice;

        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock);
        var result = await service.GetPdfAsync(ownerId, invoice.TAX_INVOICE_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Bytes);
        Assert.NotEmpty(result.Value.Bytes);
        Assert.Equal("tax-invoice-INV-555.pdf", result.Value.FileName);
    }

    [Fact]
    public async Task GetOrderReceiptPdfAsync_WhenOrderPaid_ReturnsReceiptPdf()
    {
        var invoiceRepo = new FakeTaxInvoiceRepository();
        var orderRepo = new FakeOrderRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var order = ORDER.Create("ORD-777", ownerId, 1000m, 0m, 70m, 1070m);
        order.AddItem(Guid.NewGuid(), "Design Patterns in C#", 1000m, 1000m);
        order.MarkAwaitingPayment();
        order.MarkPaid(clock);
        await orderRepo.AddAsync(order, CancellationToken.None);

        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock);
        var result = await service.GetOrderReceiptPdfAsync(ownerId, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Bytes);
        Assert.NotEmpty(result.Value.Bytes);
        Assert.Equal("receipt-ORD-777.pdf", result.Value.FileName);
    }

    [Fact]
    public async Task GetOrderReceiptPdfAsync_WhenOrderPending_ReturnsConflict()
    {
        var invoiceRepo = new FakeTaxInvoiceRepository();
        var orderRepo = new FakeOrderRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var order = ORDER.Create("ORD-888", ownerId, 1000m, 0m, 70m, 1070m);
        await orderRepo.AddAsync(order, CancellationToken.None);

        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock);
        var result = await service.GetOrderReceiptPdfAsync(ownerId, order.ORDER_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error.Code);
    }
}
