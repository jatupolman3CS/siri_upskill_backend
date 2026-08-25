using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Domain;

/// <summary>
/// One monthly payout run, and the aggregate root for its <see cref="Items"/>.
/// </summary>
public sealed class PAYOUT_BATCH : IAuditable
{
    private readonly List<PAYOUT_BATCH_ITEM> _items = [];

    private PAYOUT_BATCH()
    {
    }

    public Guid PAYOUT_BATCH_ID { get; private set; }

    public string PERIOD_KEY { get; private set; } = string.Empty;

    public decimal TOTAL_AMOUNT { get; private set; }

    public PayoutBatchStatus STATUS { get; private set; }

    public DateTime? EXECUTED_AT_UTC { get; private set; }

    public Guid? EXECUTED_BY_USER_ID { get; private set; }

    public IReadOnlyCollection<PAYOUT_BATCH_ITEM> Items => _items.AsReadOnly();

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

    public static PAYOUT_BATCH Create(string periodKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(periodKey);

        return new PAYOUT_BATCH
        {
            PAYOUT_BATCH_ID = UuidV7.NewId(),
            PERIOD_KEY = periodKey.Trim(),
            STATUS = PayoutBatchStatus.Draft,
            TOTAL_AMOUNT = 0m,
        };
    }

    public PAYOUT_BATCH_ITEM AddItem(Guid instructorId, decimal amount, decimal withholdingTaxAmount, decimal netAmount)
    {
        var item = PAYOUT_BATCH_ITEM.Create(PAYOUT_BATCH_ID, instructorId, amount, withholdingTaxAmount, netAmount);
        _items.Add(item);
        TOTAL_AMOUNT += netAmount;
        return item;
    }
}
