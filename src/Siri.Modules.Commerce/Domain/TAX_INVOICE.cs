using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// An e-Tax invoice issued for a paid <see cref="ORDER"/> — a new table per docs/DECISIONS.md D-17's
/// gap-fill (the mockup handoff needed this and docs/DATABASE.md's original sketch had no such table).
/// Automatic submission to the Thai Revenue Department is explicitly out of scope (D-17: "ตัดส่วน
/// 'นำส่งกรมสรรพากรอัตโนมัติ' ออกจาก scope — เป็น government e-filing integration แยกต่างหาก") — this
/// entity only models issuing/voiding our own record of the invoice.
/// <para>
/// Naming: SCREAMING_SNAKE_CASE class/properties, DB table/columns — the same brand-new-module
/// exception documented in full on <see cref="ORDER"/>'s doc comment (docs/DECISIONS.md D-17).
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>: <see cref="ISSUED_AT_UTC"/> already is the
/// creation moment — there is no "updated" concept for an issued invoice, only <see cref="Void"/>
/// (tracked via <see cref="STATUS"/>, not a timestamp).
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.ISoftDelete"/>: one of the four money tables this scaffold
/// pass applies database.md's "ห้าม hard delete ข้อมูลการเงิน" rule to (Orders, Payments, Refunds,
/// TaxInvoices) — <see cref="Void"/> (→ <see cref="TaxInvoiceStatus.Voided"/>) is the only way this
/// row's meaning changes, a real Thai tax invoice is never deleted, only voided/superseded.
/// </para>
/// <para>
/// <see cref="TAX_ID_ENCRYPTED"/> deliberately keeps the PascalCase-flavoured property name
/// <c>TaxIdEncrypted</c> mapped mechanically to <c>TAX_ID_ENCRYPTED</c> (not, say, <c>TAX_ID</c>) so its
/// name mirrors <c>payout.InstructorPayoutAccounts.AccountNoEncrypted</c>'s existing "*Encrypted" naming
/// convention for at-rest-encrypted columns (docs/DATABASE.md's "payout" section). No actual encryption
/// is implemented in this scaffold pass — the value is stored as plain text for now, exactly as
/// received — that is a deliberate later task, not an oversight; see the property's own doc comment.
/// </para>
/// </summary>
public sealed class TAX_INVOICE
{
    private TAX_INVOICE()
    {
    }

    public Guid TAX_INVOICE_ID { get; private set; }
    public Guid ORDER_ID { get; private set; }

    /// <summary>Holds an ENCRYPTED value at rest per security.md's rule on tax-ID numbers ("เข้ารหัสก่อน
    /// เก็บ: ... เลขประจำตัวผู้เสียภาษี") — encryption itself is a later task (this scaffold only shapes
    /// the column/property; nothing in <see cref="Issue"/> below actually encrypts <paramref name="taxId"/>
    /// yet). Do not wire a real issue-invoice flow to this method without adding that encryption step
    /// first — shipping this as-is would store the buyer's tax ID in plain text, a direct security.md
    /// violation.</summary>
    public string TAX_ID_ENCRYPTED { get; private set; } = string.Empty;

    public string BUYER_NAME { get; private set; } = string.Empty;
    public string INVOICE_NO { get; private set; } = string.Empty;
    public DateTime ISSUED_AT_UTC { get; private set; }
    public string? PDF_STORAGE_KEY { get; private set; }
    public TaxInvoiceStatus STATUS { get; private set; }

    public static TAX_INVOICE Issue(Guid orderId, string taxId, string buyerName, string invoiceNo, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taxId);
        ArgumentException.ThrowIfNullOrWhiteSpace(buyerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNo);
        ArgumentNullException.ThrowIfNull(clock);

        return new TAX_INVOICE
        {
            TAX_INVOICE_ID = UuidV7.NewId(), ORDER_ID = orderId, TAX_ID_ENCRYPTED = taxId, BUYER_NAME = buyerName,
            INVOICE_NO = invoiceNo, ISSUED_AT_UTC = clock.UtcNow, STATUS = TaxInvoiceStatus.Issued,
        };
    }

    public void AttachPdf(string pdfStorageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfStorageKey);
        PDF_STORAGE_KEY = pdfStorageKey;
    }

    public void Void()
    {
        if (STATUS != TaxInvoiceStatus.Issued) throw new InvalidOperationException($"Cannot void a tax invoice in {STATUS} status.");
        STATUS = TaxInvoiceStatus.Voided;
    }
}
