using Microsoft.EntityFrameworkCore;
using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Business logic for <see cref="PAYOUT_BATCH"/>, backing <c>PayoutBatchEndpoints</c>.
/// </summary>
public sealed class PayoutBatchService
{
    private readonly IPayoutBatchRepository _batchRepository;
    private readonly IPayoutBatchItemRepository _itemRepository;

    public PayoutBatchService(IPayoutBatchRepository batchRepository, IPayoutBatchItemRepository itemRepository)
    {
        _batchRepository = batchRepository;
        _itemRepository = itemRepository;
    }

    public async Task<Result<PayoutBatchResponse>> CreateAsync(CreatePayoutBatchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var batch = PAYOUT_BATCH.Create(command.PeriodKey);
        _batchRepository.Add(batch);
        await _batchRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(batch));
    }

    public async Task<Result<PayoutBatchResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var batch = await _batchRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            return Result.Failure<PayoutBatchResponse>(DomainError.NotFound("ไม่พบรอบการจ่ายเงิน"));
        }

        return Result.Success(ToResponse(batch));
    }

    public async Task<PagedResult<PayoutBatchResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var query = _batchRepository.Query();
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(b => b.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<PayoutBatchResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    private static PayoutBatchResponse ToResponse(PAYOUT_BATCH b) =>
        new(
            b.PAYOUT_BATCH_ID,
            b.PERIOD_KEY,
            b.TOTAL_AMOUNT,
            b.STATUS,
            b.CreatedAtUtc,
            b.EXECUTED_AT_UTC,
            b.EXECUTED_BY_USER_ID,
            b.Items.Select(i => new PayoutBatchItemResponse(
                i.PAYOUT_BATCH_ITEM_ID,
                i.INSTRUCTOR_ID,
                i.AMOUNT,
                i.WITHHOLDING_TAX_AMOUNT,
                i.NET_AMOUNT,
                i.STATUS,
                i.TRANSFER_REF)).ToList());
}
