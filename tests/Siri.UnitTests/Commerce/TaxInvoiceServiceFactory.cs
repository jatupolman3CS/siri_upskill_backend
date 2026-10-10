using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Modules.Commerce;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Identity.Contracts;
using Siri.SharedKernel;

namespace Siri.UnitTests.Commerce;

/// <summary>Builds a <see cref="TaxInvoiceService"/> with a fully configured seller identity and a
/// resolvable buyer account unless a test deliberately overrides them.</summary>
internal static class TaxInvoiceServiceFactory
{
    public static ReceiptSellerOptions ConfiguredSeller() => new()
    {
        CompanyName = "Test Seller Co., Ltd.",
        BranchLabel = "Head Office",
        TaxId = "0105500000001",
        Address = "1 Test Road, Bangkok",
        ContactEmail = "billing@seller.test",
        Website = "https://seller.test",
    };

    public static TaxInvoiceService Create(
        ITaxInvoiceRepository invoiceRepository,
        IOrderRepository orderRepository,
        IClock clock,
        IUserContactReader? contactReader = null,
        ReceiptSellerOptions? seller = null,
        IPaymentRepository? paymentRepository = null) =>
        new(
            invoiceRepository,
            orderRepository,
            paymentRepository ?? new InMemoryPaymentRepository(),
            contactReader ?? new FakeUserContactReader("buyer@example.test", "Real Buyer"),
            Options.Create(seller ?? ConfiguredSeller()),
            clock,
            NullLogger<TaxInvoiceService>.Instance);

    /// <summary>Minimal in-memory payments store — empty by default, which means "no succeeded payment" and so
    /// "not paid through an amount override" for tests that don't care about the override guard.</summary>
    internal sealed class InMemoryPaymentRepository : IPaymentRepository
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
            Task.FromResult<IReadOnlyList<PAYMENT>>(_payments
                .Where(p => p.ORDER_ID == orderId && (p.STATUS == PaymentStatus.Pending || p.STATUS == PaymentStatus.Processing))
                .ToList());

        public Task<IReadOnlyDictionary<Guid, Guid>> GetSucceededPaymentIdsByOrderIdsAsync(
            IReadOnlyCollection<Guid> orderIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(_payments
                .Where(p => orderIds.Contains(p.ORDER_ID) && p.STATUS == PaymentStatus.Succeeded)
                .GroupBy(p => p.ORDER_ID)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CREATED_AT_UTC).First().PAYMENT_ID));
    }

    internal sealed class FakeUserContactReader(string? email, string? displayName) : IUserContactReader
    {
        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(email);

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult((email, displayName));
    }
}
