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

        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock);
        var command = new IssueTaxInvoiceCommand(order.ORDER_ID, "0105558123456", "บริษัท ตัวอย่าง จำกัด");

        var result = await service.IssueAsync(userId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("บริษัท ตัวอย่าง จำกัด", result.Value.BuyerName);
        Assert.StartsWith("INV-260821-", result.Value.InvoiceNo);
        Assert.Equal(TaxInvoiceStatus.Issued, result.Value.Status);
        Assert.Single(invoiceRepo.Invoices);
    }

    private static async Task<(TaxInvoiceService Service, ORDER Order, Guid UserId, FakeTaxInvoiceRepository Invoices)> PaidOrderWithPaymentAsync(
        decimal? originalAmount)
    {
        var invoiceRepo = new FakeTaxInvoiceRepository();
        var orderRepo = new FakeOrderRepository();
        var paymentRepo = new TaxInvoiceServiceFactory.InMemoryPaymentRepository();
        var clock = new FakeClock(new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc));

        var userId = Guid.NewGuid();
        var order = ORDER.Create("ORD-OVR-DOC", userId, 1890m, 0m, 0m, 1890m);
        order.MarkAwaitingPayment();
        order.MarkPaid(clock);
        await orderRepo.AddAsync(order, CancellationToken.None);

        var payment = originalAmount is null
            ? PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_doc", 1890m, clock)
            : PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_doc", 20m, clock, originalAmount);
        payment.MarkSucceeded(clock);
        await paymentRepo.AddAsync(payment, CancellationToken.None);

        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock, paymentRepository: paymentRepo);
        return (service, order, userId, invoiceRepo);
    }

    [Fact]
    public async Task IssueAsync_OrderPaidThroughAmountOverride_RefusesBecauseDocumentWouldOverstatePaidAmount()
    {
        var (service, order, userId, invoices) = await PaidOrderWithPaymentAsync(originalAmount: 1890m);

        var result = await service.IssueAsync(userId, new IssueTaxInvoiceCommand(order.ORDER_ID, "0105558123456", "บริษัท ตัวอย่าง จำกัด"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(invoices.Invoices);
    }

    [Fact]
    public async Task IssueAsync_OrderPaidNormally_StillIssuesWhenAPaymentExists()
    {
        var (service, order, userId, invoices) = await PaidOrderWithPaymentAsync(originalAmount: null);

        var result = await service.IssueAsync(userId, new IssueTaxInvoiceCommand(order.ORDER_ID, "0105558123456", "บริษัท ตัวอย่าง จำกัด"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(invoices.Invoices);
    }

    [Fact]
    public async Task GetOrderReceiptPdfAsync_OrderPaidThroughAmountOverride_RefusesWithConflict()
    {
        var (service, order, userId, _) = await PaidOrderWithPaymentAsync(originalAmount: 1890m);

        var result = await service.GetOrderReceiptPdfAsync(userId, order.ORDER_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error.Code);
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

        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock);
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

        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock);

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

        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock);
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

        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock);
        var result = await service.GetByOrderIdAsync(strangerId, order.ORDER_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static async Task<(FakeTaxInvoiceRepository InvoiceRepo, FakeOrderRepository OrderRepo, ORDER Order, Guid OwnerId, FakeClock Clock)> ArrangePaidOrderAsync(decimal total = 1070m)
    {
        var invoiceRepo = new FakeTaxInvoiceRepository();
        var orderRepo = new FakeOrderRepository();
        var clock = new FakeClock(new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc));
        var ownerId = Guid.NewGuid();
        var order = ORDER.Create("ORD-REAL-1", ownerId, total, 0m, 70m, total);
        order.AddItem(Guid.NewGuid(), "Real Course Title", total, total);
        order.MarkAwaitingPayment();
        order.MarkPaid(clock);
        await orderRepo.AddAsync(order, CancellationToken.None);
        return (invoiceRepo, orderRepo, order, ownerId, clock);
    }

    [Fact]
    public async Task BuildOrderReceiptDataAsync_NoTaxInvoice_PrintsRealAccountDisplayNameAndConfiguredSeller()
    {
        var (invoiceRepo, orderRepo, order, ownerId, clock) = await ArrangePaidOrderAsync();
        var service = TaxInvoiceServiceFactory.Create(
            invoiceRepo, orderRepo, clock,
            new TaxInvoiceServiceFactory.FakeUserContactReader("somchai@example.test", "สมชาย ใจดี"));

        var result = await service.BuildOrderReceiptDataAsync(ownerId, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("สมชาย ใจดี", result.Value.BuyerName);
        Assert.NotEqual("Customer", result.Value.BuyerName);
        Assert.Equal("Test Seller Co., Ltd.", result.Value.Seller.CompanyName);
        Assert.Equal("0105500000001", result.Value.Seller.TaxId);
        Assert.Equal("1 Test Road, Bangkok", result.Value.Seller.Address);
    }

    [Fact]
    public async Task BuildOrderReceiptDataAsync_NoTaxInvoiceAndNoDisplayName_FallsBackToRealEmail()
    {
        var (invoiceRepo, orderRepo, order, ownerId, clock) = await ArrangePaidOrderAsync();
        var service = TaxInvoiceServiceFactory.Create(
            invoiceRepo, orderRepo, clock,
            new TaxInvoiceServiceFactory.FakeUserContactReader("somchai@example.test", "  "));

        var result = await service.BuildOrderReceiptDataAsync(ownerId, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("somchai@example.test", result.Value.BuyerName);
    }

    [Fact]
    public async Task BuildOrderReceiptDataAsync_BuyerAccountUnresolvable_FailsInsteadOfInventingAName()
    {
        var (invoiceRepo, orderRepo, order, ownerId, clock) = await ArrangePaidOrderAsync();
        var service = TaxInvoiceServiceFactory.Create(
            invoiceRepo, orderRepo, clock,
            new TaxInvoiceServiceFactory.FakeUserContactReader(null, null));

        var result = await service.BuildOrderReceiptDataAsync(ownerId, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);

        var pdf = await service.GetOrderReceiptPdfAsync(ownerId, order.ORDER_ID, CancellationToken.None);
        Assert.True(pdf.IsFailure);
    }

    [Fact]
    public async Task BuildOrderReceiptDataAsync_WithTaxInvoice_UsesInvoiceBuyerNameNotAccountName()
    {
        var (invoiceRepo, orderRepo, order, ownerId, clock) = await ArrangePaidOrderAsync();
        var invoice = TAX_INVOICE.Issue(order.ORDER_ID, "0105558123456", "บริษัท ผู้ซื้อ จำกัด", "INV-9", clock);
        invoiceRepo.Invoices[invoice.TAX_INVOICE_ID] = invoice;
        var service = TaxInvoiceServiceFactory.Create(
            invoiceRepo, orderRepo, clock,
            new TaxInvoiceServiceFactory.FakeUserContactReader("somchai@example.test", "สมชาย ใจดี"));

        var result = await service.BuildOrderReceiptDataAsync(ownerId, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("บริษัท ผู้ซื้อ จำกัด", result.Value.BuyerName);
        Assert.Equal("INV-9", result.Value.DocumentNumber);
    }

    [Theory]
    [InlineData("", "0105500000001", "1 Test Road")]
    [InlineData("Test Seller Co., Ltd.", "", "1 Test Road")]
    [InlineData("Test Seller Co., Ltd.", "123", "1 Test Road")]
    [InlineData("Test Seller Co., Ltd.", "0105500000001", "")]
    [InlineData("CHANGE_ME_DEV_ONLY", "0105500000001", "1 Test Road")]
    public async Task GetOrderReceiptPdfAsync_SellerIdentityMissingOrPlaceholder_FailsWith503Code(string company, string taxId, string address)
    {
        var (invoiceRepo, orderRepo, order, ownerId, clock) = await ArrangePaidOrderAsync();
        var seller = TaxInvoiceServiceFactory.ConfiguredSeller();
        seller.CompanyName = company;
        seller.TaxId = taxId;
        seller.Address = address;
        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock, seller: seller);

        var result = await service.GetOrderReceiptPdfAsync(ownerId, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ReceiptErrors.SellerNotConfiguredCode, result.Error.Code);
        Assert.EndsWith(DomainErrorHttpResults.NotConfiguredCodeSuffix, result.Error.Code);
    }

    [Fact]
    public async Task GetPdfAsync_SellerIdentityNotConfigured_FailsWith503Code()
    {
        var (invoiceRepo, orderRepo, order, ownerId, clock) = await ArrangePaidOrderAsync();
        var invoice = TAX_INVOICE.Issue(order.ORDER_ID, "0105558123456", "บริษัท ผู้ซื้อ จำกัด", "INV-9", clock);
        invoiceRepo.Invoices[invoice.TAX_INVOICE_ID] = invoice;
        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock, seller: new Siri.Modules.Commerce.ReceiptSellerOptions());

        var result = await service.GetPdfAsync(ownerId, invoice.TAX_INVOICE_ID, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ReceiptErrors.SellerNotConfiguredCode, result.Error.Code);
    }

    [Fact]
    public async Task GetOrderReceiptPdfAsync_StrangerWithUnconfiguredSeller_StillGetsNotFoundNotConfigState()
    {
        var (invoiceRepo, orderRepo, order, _, clock) = await ArrangePaidOrderAsync();
        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock, seller: new Siri.Modules.Commerce.ReceiptSellerOptions());

        var result = await service.GetOrderReceiptPdfAsync(Guid.NewGuid(), order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task BuildOrderReceiptDataAsync_FullyDiscountedOrder_DoesNotClaimAPaymentChannel()
    {
        var (invoiceRepo, orderRepo, order, ownerId, clock) = await ArrangePaidOrderAsync(total: 0m);
        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock);

        var result = await service.BuildOrderReceiptDataAsync(ownerId, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("-", result.Value.PaymentMethod);
    }

    [Fact]
    public async Task BuildOrderReceiptDataAsync_PaidOrder_ReportsPromptPayStripe()
    {
        var (invoiceRepo, orderRepo, order, ownerId, clock) = await ArrangePaidOrderAsync();
        var service = TaxInvoiceServiceFactory.Create(invoiceRepo, orderRepo, clock);

        var result = await service.BuildOrderReceiptDataAsync(ownerId, order.ORDER_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("PromptPay / Stripe", result.Value.PaymentMethod);
    }
}
