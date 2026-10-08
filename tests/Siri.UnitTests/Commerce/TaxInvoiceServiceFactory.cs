using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Modules.Commerce;
using Siri.Modules.Commerce.Application;
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
        ReceiptSellerOptions? seller = null) =>
        new(
            invoiceRepository,
            orderRepository,
            contactReader ?? new FakeUserContactReader("buyer@example.test", "Real Buyer"),
            Options.Create(seller ?? ConfiguredSeller()),
            clock,
            NullLogger<TaxInvoiceService>.Instance);

    internal sealed class FakeUserContactReader(string? email, string? displayName) : IUserContactReader
    {
        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(email);

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult((email, displayName));
    }
}
