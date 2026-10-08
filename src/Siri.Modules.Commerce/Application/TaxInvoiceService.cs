using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Identity.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// No <c>ITaxInvoiceService</c> interface — same reasoning as <c>OrderService</c>'s own doc comment.
/// <para>
/// <see cref="TAX_INVOICE"/> has no direct owning-user column (only <see cref="TAX_INVOICE.ORDER_ID"/>), so
/// every read joins through <see cref="IOrderRepository"/> to find the order's <c>USER_ID</c> and answers
/// 404 for anyone but the buyer (IDOR guard).
/// </para>
/// <para>
/// Real data only on the printed documents: the SELLER is the configured legal entity
/// (<see cref="ReceiptSellerOptions"/>; missing → <see cref="ReceiptErrors.SellerNotConfiguredCode"/>, never an
/// invented company) and a receipt's BUYER is the tax invoice's recorded buyer name or, for a plain receipt,
/// the account's real display name via <see cref="IUserContactReader"/> (missing → <see cref="ReceiptErrors
/// .BuyerNotFound"/>, never a placeholder like "Customer").
/// </para>
/// <para>
/// <see cref="IssueAsync"/> still stores <see cref="TAX_INVOICE.TAX_ID_ENCRYPTED"/> exactly as received —
/// that property's own doc comment records that at-rest encryption is a pending task.
/// </para>
/// </summary>
public sealed class TaxInvoiceService(
    ITaxInvoiceRepository taxInvoiceRepository,
    IOrderRepository orderRepository,
    IUserContactReader userContactReader,
    IOptions<ReceiptSellerOptions> sellerOptions,
    IClock clock,
    ILogger<TaxInvoiceService> logger)
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
        var data = await BuildTaxInvoiceDataAsync(userId, taxInvoiceId, cancellationToken).ConfigureAwait(false);
        if (data.IsFailure)
        {
            return Result.Failure<(byte[] Bytes, string FileName)>(data.Error);
        }

        var pdfBytes = Infrastructure.ReceiptPdfGenerator.GeneratePdf(data.Value);
        return Result.Success((pdfBytes, $"tax-invoice-{data.Value.DocumentNumber}.pdf"));
    }

    public async Task<Result<(byte[] Bytes, string FileName)>> GetOrderReceiptPdfAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var data = await BuildOrderReceiptDataAsync(userId, orderId, cancellationToken).ConfigureAwait(false);
        if (data.IsFailure)
        {
            return Result.Failure<(byte[] Bytes, string FileName)>(data.Error);
        }

        var pdfBytes = Infrastructure.ReceiptPdfGenerator.GeneratePdf(data.Value);
        return Result.Success((pdfBytes, $"receipt-{data.Value.OrderNo}.pdf"));
    }

    /// <summary>Everything printed on a tax-invoice PDF (ownership checked first, then the seller identity),
    /// before rendering — kept separate from the renderer so the real values can be asserted.</summary>
    internal async Task<Result<Infrastructure.ReceiptPdfData>> BuildTaxInvoiceDataAsync(Guid userId, Guid taxInvoiceId, CancellationToken cancellationToken)
    {
        var invoice = await taxInvoiceRepository.GetByIdAsync(taxInvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result.Failure<Infrastructure.ReceiptPdfData>(DomainError.NotFound("ไม่พบใบกำกับภาษี"));
        }

        var order = await orderRepository.GetByIdAsync(invoice.ORDER_ID, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<Infrastructure.ReceiptPdfData>(DomainError.NotFound("ไม่พบใบกำกับภาษี"));
        }

        var seller = ResolveSeller();
        if (seller.IsFailure)
        {
            return Result.Failure<Infrastructure.ReceiptPdfData>(seller.Error);
        }

        return Result.Success(new Infrastructure.ReceiptPdfData(
            "TAX INVOICE / RECEIPT",
            invoice.INVOICE_NO,
            order.ORDER_NO,
            invoice.BUYER_NAME,
            invoice.TAX_ID_ENCRYPTED,
            null,
            invoice.ISSUED_AT_UTC,
            PaymentMethodLabel(order),
            ToPdfItems(order),
            order.SUBTOTAL_AMOUNT,
            order.DISCOUNT_AMOUNT,
            order.TAX_AMOUNT,
            order.TOTAL_AMOUNT,
            seller.Value));
    }

    /// <summary>Everything printed on an order receipt PDF, before rendering — see
    /// <see cref="BuildTaxInvoiceDataAsync"/>. The buyer is the name recorded on the tax invoice when one
    /// exists; otherwise the account's real display name (falling back to its real email). No invented name:
    /// an unresolvable buyer fails.</summary>
    internal async Task<Result<Infrastructure.ReceiptPdfData>> BuildOrderReceiptDataAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<Infrastructure.ReceiptPdfData>(DomainError.NotFound("ไม่พบคำสั่งซื้อ"));
        }

        if (order.STATUS != OrderStatus.Paid)
        {
            return Result.Failure<Infrastructure.ReceiptPdfData>(DomainError.Conflict("สามารถดาวน์โหลดใบเสร็จได้เฉพาะคำสั่งซื้อที่ชำระเงินแล้วเท่านั้น"));
        }

        var seller = ResolveSeller();
        if (seller.IsFailure)
        {
            return Result.Failure<Infrastructure.ReceiptPdfData>(seller.Error);
        }

        var invoice = await taxInvoiceRepository.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        var docNo = invoice?.INVOICE_NO ?? $"REC-{order.ORDER_NO}";

        string buyerName;
        if (invoice is not null)
        {
            buyerName = invoice.BUYER_NAME;
        }
        else
        {
            var (email, displayName) = await userContactReader
                .GetUserContactInfoAsync(order.USER_ID, cancellationToken)
                .ConfigureAwait(false);

            var resolved = !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim() : email?.Trim();
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return Result.Failure<Infrastructure.ReceiptPdfData>(ReceiptErrors.BuyerNotFound());
            }

            buyerName = resolved;
        }

        return Result.Success(new Infrastructure.ReceiptPdfData(
            invoice != null ? "TAX INVOICE / RECEIPT" : "OFFICIAL RECEIPT",
            docNo,
            order.ORDER_NO,
            buyerName,
            invoice?.TAX_ID_ENCRYPTED,
            null,
            order.PAID_AT_UTC ?? order.CreatedAtUtc,
            PaymentMethodLabel(order),
            ToPdfItems(order),
            order.SUBTOTAL_AMOUNT,
            order.DISCOUNT_AMOUNT,
            order.TAX_AMOUNT,
            order.TOTAL_AMOUNT,
            seller.Value));
    }

    private static List<Infrastructure.ReceiptPdfItem> ToPdfItems(ORDER order) =>
        order.ORDER_ITEMS.Select(i => new Infrastructure.ReceiptPdfItem(
            i.TITLE_SNAPSHOT,
            1,
            i.UNIT_PRICE,
            i.LINE_TOTAL)).ToList();

    /// <summary>
    /// What the "paid via" line says. A paying order was settled through Stripe PromptPay (the only enabled
    /// method in v1 — D-14); a fully-discounted (฿0) order took no payment at all, so claiming a payment
    /// channel on it would be false — it prints a neutral dash instead.
    /// </summary>
    private static string PaymentMethodLabel(ORDER order) =>
        order.TOTAL_AMOUNT > 0m ? "PromptPay / Stripe" : "-";

    /// <summary>The configured seller identity for the document header, or
    /// <see cref="ReceiptErrors.SellerNotConfigured"/> when it is missing/placeholder (nothing is invented).</summary>
    private Result<Infrastructure.ReceiptSellerInfo> ResolveSeller()
    {
        var options = sellerOptions.Value;

        var missing = options.GetMissingSettings();
        if (missing.Count > 0)
        {
            logger.LogError(
                "Receipt not generated: the seller identity is not configured (missing/placeholder: {MissingSettings}).",
                missing);
            return Result.Failure<Infrastructure.ReceiptSellerInfo>(ReceiptErrors.SellerNotConfigured());
        }

        return Result.Success(new Infrastructure.ReceiptSellerInfo(
            options.CompanyName.Trim(),
            string.IsNullOrWhiteSpace(options.BranchLabel) ? null : options.BranchLabel.Trim(),
            options.TaxId.Trim(),
            options.Address.Trim(),
            string.IsNullOrWhiteSpace(options.ContactEmail) ? null : options.ContactEmail.Trim(),
            string.IsNullOrWhiteSpace(options.Website) ? null : options.Website.Trim()));
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
