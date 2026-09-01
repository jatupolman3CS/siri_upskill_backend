using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Domain;

/// <summary>
/// A per-order-item revenue split between the platform and an instructor.
/// </summary>
public sealed class REVENUE_SPLIT : IAuditable
{
    private REVENUE_SPLIT()
    {
    }

    public Guid REVENUE_SPLIT_ID { get; private set; }

    public Guid ORDER_ITEM_ID { get; private set; }

    public Guid INSTRUCTOR_ID { get; private set; }

    public decimal GROSS_AMOUNT { get; private set; }

    public decimal PAYMENT_FEE_AMOUNT { get; private set; }

    public decimal PLATFORM_FEE_AMOUNT { get; private set; }

    public decimal INSTRUCTOR_AMOUNT { get; private set; }

    public decimal REVENUE_SHARE_PERCENT { get; private set; }

    public string PERIOD_KEY { get; private set; } = string.Empty;

    public RevenueSplitStatus STATUS { get; private set; }

    public Guid? PAYOUT_BATCH_ITEM_ID { get; private set; }

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

    public static REVENUE_SPLIT Create(
        Guid orderItemId,
        Guid instructorId,
        decimal grossAmount,
        decimal paymentFeeAmount,
        decimal platformFeeAmount,
        decimal instructorAmount,
        decimal revenueSharePercent,
        string periodKey)
    {
        if (orderItemId == Guid.Empty) throw new ArgumentException("Order item ID cannot be empty.", nameof(orderItemId));
        if (instructorId == Guid.Empty) throw new ArgumentException("Instructor ID cannot be empty.", nameof(instructorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(periodKey);

        return new REVENUE_SPLIT
        {
            REVENUE_SPLIT_ID = UuidV7.NewId(),
            ORDER_ITEM_ID = orderItemId,
            INSTRUCTOR_ID = instructorId,
            GROSS_AMOUNT = grossAmount,
            PAYMENT_FEE_AMOUNT = paymentFeeAmount,
            PLATFORM_FEE_AMOUNT = platformFeeAmount,
            INSTRUCTOR_AMOUNT = instructorAmount,
            REVENUE_SHARE_PERCENT = revenueSharePercent,
            PERIOD_KEY = periodKey.Trim(),
            STATUS = RevenueSplitStatus.Pending,
        };
    }

    public static REVENUE_SPLIT CreateAdjustment(
        Guid orderItemId,
        Guid instructorId,
        decimal grossAmount,
        decimal paymentFeeAmount,
        decimal platformFeeAmount,
        decimal instructorAmount,
        decimal revenueSharePercent,
        string periodKey)
    {
        if (orderItemId == Guid.Empty) throw new ArgumentException("Order item ID cannot be empty.", nameof(orderItemId));
        if (instructorId == Guid.Empty) throw new ArgumentException("Instructor ID cannot be empty.", nameof(instructorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(periodKey);

        return new REVENUE_SPLIT
        {
            REVENUE_SPLIT_ID = UuidV7.NewId(),
            ORDER_ITEM_ID = orderItemId,
            INSTRUCTOR_ID = instructorId,
            GROSS_AMOUNT = grossAmount,
            PAYMENT_FEE_AMOUNT = paymentFeeAmount,
            PLATFORM_FEE_AMOUNT = platformFeeAmount,
            INSTRUCTOR_AMOUNT = instructorAmount,
            REVENUE_SHARE_PERCENT = revenueSharePercent,
            PERIOD_KEY = periodKey.Trim(),
            STATUS = RevenueSplitStatus.Pending,
        };
    }

    public void MarkPayable()
    {
        if (STATUS == RevenueSplitStatus.Pending)
        {
            STATUS = RevenueSplitStatus.Payable;
        }
    }

    /// <summary>
    /// Links this split to the <see cref="PAYOUT_BATCH_ITEM"/> it was grouped into during payout-batch
    /// creation (<see cref="PayoutBatchStatus.Draft"/> aggregation) — deliberately does not change
    /// <see cref="STATUS"/>; that only transitions to <see cref="RevenueSplitStatus.Paid"/> later,
    /// atomically with actual execution, via <see cref="MarkPaid"/>. Exists so callers never have to
    /// reach past <see cref="PAYOUT_BATCH_ITEM_ID"/>'s private setter with reflection to record this
    /// linkage — see <c>PayoutBatchService.CreateAsync</c>'s own comment for the incident that made this
    /// method necessary.
    /// </summary>
    public void AssignToBatchItem(Guid payoutBatchItemId)
    {
        if (payoutBatchItemId == Guid.Empty)
        {
            throw new ArgumentException("Payout batch item ID cannot be empty.", nameof(payoutBatchItemId));
        }

        PAYOUT_BATCH_ITEM_ID = payoutBatchItemId;
    }

    public void MarkPaid(Guid payoutBatchItemId)
    {
        if (payoutBatchItemId == Guid.Empty) throw new ArgumentException("Payout batch item ID cannot be empty.", nameof(payoutBatchItemId));
        STATUS = RevenueSplitStatus.Paid;
        PAYOUT_BATCH_ITEM_ID = payoutBatchItemId;
    }

    public void Reverse()
    {
        if (STATUS == RevenueSplitStatus.Paid)
        {
            throw new InvalidOperationException("Cannot reverse a split that has already been marked as Paid. Create a negative adjustment split instead.");
        }
        STATUS = RevenueSplitStatus.Reversed;
    }
}
