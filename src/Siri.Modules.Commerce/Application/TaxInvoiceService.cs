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

    public async Task<Result<TaxInvoiceResponse>> GetByOrderIdAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<TaxInvoiceResponse>(DomainError.NotFound("ไม่พบคำสั่งซื้อ"));
        }

        var invoice = await taxInvoiceRepository.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result.Failure<TaxInvoiceResponse>(DomainError.NotFound("ไม่พบใบกำกับภาษีสำหรับคำสั่งซื้อนี้"));
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

    public async Task<Result<(byte[] Bytes, string FileName)>> GetPdfAsync(Guid userId, Guid taxInvoiceId, CancellationToken cancellationToken)
    {
        var invoice = await taxInvoiceRepository.GetByIdAsync(taxInvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result.Failure<(byte[] Bytes, string FileName)>(DomainError.NotFound("ไม่พบใบกำกับภาษี"));
        }

        var order = await orderRepository.GetByIdAsync(invoice.ORDER_ID, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<(byte[] Bytes, string FileName)>(DomainError.NotFound("ไม่พบใบกำกับภาษี"));
        }

        var items = order.ORDER_ITEMS.Select(i => new Infrastructure.ReceiptPdfItem(
            i.TITLE_SNAPSHOT,
            1,
            i.UNIT_PRICE,
            i.LINE_TOTAL)).ToList();

        var pdfData = new Infrastructure.ReceiptPdfData(
            "TAX INVOICE / RECEIPT",
            invoice.INVOICE_NO,
            order.ORDER_NO,
            invoice.BUYER_NAME,
            invoice.TAX_ID_ENCRYPTED,
            null,
            invoice.ISSUED_AT_UTC,
            "PromptPay / Stripe",
            items,
            order.SUBTOTAL_AMOUNT,
            order.DISCOUNT_AMOUNT,
            order.TAX_AMOUNT,
            order.TOTAL_AMOUNT);

        var pdfBytes = Infrastructure.ReceiptPdfGenerator.GeneratePdf(pdfData);
        return Result.Success((pdfBytes, $"tax-invoice-{invoice.INVOICE_NO}.pdf"));
    }

    public async Task<Result<(byte[] Bytes, string FileName)>> GetOrderReceiptPdfAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<(byte[] Bytes, string FileName)>(DomainError.NotFound("ไม่พบคำสั่งซื้อ"));
        }

        if (order.STATUS != OrderStatus.Paid)
        {
            return Result.Failure<(byte[] Bytes, string FileName)>(DomainError.Conflict("สามารถดาวน์โหลดใบเสร็จได้เฉพาะคำสั่งซื้อที่ชำระเงินแล้วเท่านั้น"));
        }

        var invoice = await taxInvoiceRepository.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        var docNo = invoice?.INVOICE_NO ?? $"REC-{order.ORDER_NO}";
        var buyerName = invoice?.BUYER_NAME ?? "Customer";

        var items = order.ORDER_ITEMS.Select(i => new Infrastructure.ReceiptPdfItem(
            i.TITLE_SNAPSHOT,
            1,
            i.UNIT_PRICE,
            i.LINE_TOTAL)).ToList();

        var pdfData = new Infrastructure.ReceiptPdfData(
            invoice != null ? "TAX INVOICE / RECEIPT" : "OFFICIAL RECEIPT",
            docNo,
            order.ORDER_NO,
            buyerName,
            invoice?.TAX_ID_ENCRYPTED,
            null,
            order.PAID_AT_UTC ?? order.CreatedAtUtc,
            "PromptPay / Stripe",
            items,
            order.SUBTOTAL_AMOUNT,
            order.DISCOUNT_AMOUNT,
            order.TAX_AMOUNT,
            order.TOTAL_AMOUNT);

        var pdfBytes = Infrastructure.ReceiptPdfGenerator.GeneratePdf(pdfData);
        return Result.Success((pdfBytes, $"receipt-{order.ORDER_NO}.pdf"));
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
