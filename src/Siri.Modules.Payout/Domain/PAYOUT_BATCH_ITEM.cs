using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Domain;

/// <summary>
/// One instructor's line item within a <see cref="PAYOUT_BATCH"/>.
/// </summary>
public sealed class PAYOUT_BATCH_ITEM : IAuditable
{
    private PAYOUT_BATCH_ITEM()
    {
    }

    public Guid PAYOUT_BATCH_ITEM_ID { get; private set; }

    public Guid BATCH_ID { get; private set; }

    public Guid INSTRUCTOR_ID { get; private set; }

    public decimal AMOUNT { get; private set; }

    public decimal WITHHOLDING_TAX_PERCENT { get; private set; }

    public decimal WITHHOLDING_TAX_AMOUNT { get; private set; }

    public decimal NET_AMOUNT { get; private set; }

    public PayoutBatchItemStatus STATUS { get; private set; }

    public string? TRANSFER_REF { get; private set; }

    // ---- IAuditable -----------------------------------------------------------------------------
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    internal static PAYOUT_BATCH_ITEM Create(
        Guid batchId,
        Guid instructorId,
        decimal amount,
        decimal withholdingTaxPercent,
        decimal withholdingTaxAmount,
        decimal netAmount)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("Batch ID cannot be empty.", nameof(batchId));
        if (instructorId == Guid.Empty) throw new ArgumentException("Instructor ID cannot be empty.", nameof(instructorId));

        return new PAYOUT_BATCH_ITEM
        {
            PAYOUT_BATCH_ITEM_ID = UuidV7.NewId(),
            BATCH_ID = batchId,
            INSTRUCTOR_ID = instructorId,
            AMOUNT = amount,
            WITHHOLDING_TAX_PERCENT = withholdingTaxPercent,
            WITHHOLDING_TAX_AMOUNT = withholdingTaxAmount,
            NET_AMOUNT = netAmount,
            STATUS = PayoutBatchItemStatus.Pending,
        };
    }

    public void MarkTransferred(string? transferRef = null)
    {
        STATUS = PayoutBatchItemStatus.Transferred;
        TRANSFER_REF = transferRef?.Trim();
    }

    public void MarkFailed()
    {
        STATUS = PayoutBatchItemStatus.Failed;
    }
}
