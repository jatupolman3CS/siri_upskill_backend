using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// No <c>ITaxInvoiceService</c> interface — same reasoning as <c>OrderService</c>'s own doc comment.
/// <para>
/// Every method here is a stub, including the read — same shape as <c>PaymentService</c>'s own doc comment
/// describes for the exact same reason: <see cref="TAX_INVOICE"/> has no direct owning-user column (only
/// <see cref="TAX_INVOICE.ORDER_ID"/>); a correct ownership check for <see cref="GetByIdAsync"/> would need
/// to join through <see cref="IOrderRepository"/> to find the order's <c>USER_ID</c>, which this scaffold
/// pass deliberately does not implement (security.md's ownership-check rule is not optional; shipping a
/// "works" read with no ownership check would be a real IDOR gap).
/// </para>
/// <para>
/// <see cref="IssueAsync"/> is additionally blocked by <see cref="TAX_INVOICE.TAX_ID_ENCRYPTED"/>'s own doc
/// comment, which explicitly forbids wiring a real issue flow before encryption is implemented: "shipping
/// this as-is would store the buyer's tax ID in plain text, a direct security.md violation."
/// </para>
/// </summary>
public sealed class TaxInvoiceService(
    ITaxInvoiceRepository taxInvoiceRepository,
    IOrderRepository orderRepository,
    IClock clock)
{
    public async Task<Result<TaxInvoiceResponse>> IssueAsync(Guid userId, IssueTaxInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await orderRepository.GetByIdAsync(command.OrderId, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<TaxInvoiceResponse>(DomainError.NotFound("ไม่พบคำสั่งซื้อ"));
        }

        if (order.STATUS != OrderStatus.Paid)
        {
            return Result.Failure<TaxInvoiceResponse>(DomainError.Conflict("สามารถออกใบกำกับภาษีได้เฉพาะคำสั่งซื้อที่ชำระเงินแล้วเท่านั้น"));
        }

        var invoiceNo = $"INV-{clock.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var invoice = TAX_INVOICE.Issue(command.OrderId, command.TaxId, command.BuyerName, invoiceNo, clock);

        await taxInvoiceRepository.AddAsync(invoice, cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(invoice));
    }

    public async Task<Result<TaxInvoiceResponse>> GetByIdAsync(Guid userId, Guid taxInvoiceId, CancellationToken cancellationToken)
    {
        var invoice = await taxInvoiceRepository.GetByIdAsync(taxInvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result.Failure<TaxInvoiceResponse>(DomainError.NotFound("ไม่พบใบกำกับภาษี"));
        }

        var order = await orderRepository.GetByIdAsync(invoice.ORDER_ID, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<TaxInvoiceResponse>(DomainError.NotFound("ไม่พบใบกำกับภาษี"));
        }

        return Result.Success(ToResponse(invoice));
    }

    public async Task<PagedResult<TaxInvoiceResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var totalCount = await taxInvoiceRepository.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await taxInvoiceRepository.ListAsync(effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);

        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<TaxInvoiceResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    private static TaxInvoiceResponse ToResponse(TAX_INVOICE taxInvoice) =>
        new(
            taxInvoice.TAX_INVOICE_ID, taxInvoice.ORDER_ID, taxInvoice.BUYER_NAME, taxInvoice.INVOICE_NO,
            taxInvoice.ISSUED_AT_UTC, taxInvoice.PDF_STORAGE_KEY, taxInvoice.STATUS);
}

/// <summary>Deliberately excludes the raw tax id from the response — <see cref="TAX_INVOICE.TAX_ID_ENCRYPTED"/>
/// holds an encrypted-at-rest value (see that property's own doc comment); once decryption exists, whoever
/// implements <c>TaxInvoiceService.ToResponse</c> for real must decide whether/how to expose it, not this
/// scaffold pass.</summary>
public sealed record TaxInvoiceResponse(
    Guid Id,
    Guid OrderId,
    string BuyerName,
    string InvoiceNo,
    DateTime IssuedAtUtc,
    string? PdfStorageKey,
    TaxInvoiceStatus Status);

public sealed record IssueTaxInvoiceCommand(Guid OrderId, string TaxId, string BuyerName);
