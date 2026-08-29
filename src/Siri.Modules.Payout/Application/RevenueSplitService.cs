using Microsoft.EntityFrameworkCore;
using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Business logic for <see cref="REVENUE_SPLIT"/>, backing <c>RevenueSplitEndpoints</c>.
/// </summary>
public sealed class RevenueSplitService(IRevenueSplitRepository repository)
{
    public async Task<Result<RevenueSplitResponse>> CreateAsync(CreateRevenueSplitCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await repository.GetByOrderItemIdAsync(command.OrderItemId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Failure<RevenueSplitResponse>(DomainError.Conflict("มีข้อมูลส่วนแบ่งรายได้สำหรับรายการสั่งซื้อนี้แล้ว"));
        }

        var split = REVENUE_SPLIT.Create(
            command.OrderItemId,
            command.InstructorId,
            command.GrossAmount,
            command.PaymentFeeAmount,
            command.PlatformFeeAmount,
            command.InstructorAmount,
            command.RevenueSharePercent,
            command.PeriodKey);

        repository.Add(split);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(split));
    }

    public async Task<Result<RevenueSplitResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var split = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (split is null)
        {
            return Result.Failure<RevenueSplitResponse>(DomainError.NotFound("ไม่พบข้อมูลส่วนแบ่งรายได้"));
        }

        return Result.Success(ToResponse(split));
    }

    public async Task<PagedResult<RevenueSplitResponse>> ListAsync(
        Guid? instructorId,
        string? periodKey,
        RevenueSplitStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var query = repository.Query();

        if (instructorId.HasValue)
        {
            query = query.Where(r => r.INSTRUCTOR_ID == instructorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(periodKey))
        {
            query = query.Where(r => r.PERIOD_KEY == periodKey.Trim());
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.STATUS == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<RevenueSplitResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    public async Task<PagedResult<RevenueSplitResponse>> ListForInstructorAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var query = repository.Query().Where(r => r.INSTRUCTOR_ID == userId);
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<RevenueSplitResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    private static RevenueSplitResponse ToResponse(REVENUE_SPLIT r) =>
        new(
            r.REVENUE_SPLIT_ID,
            r.ORDER_ITEM_ID,
            r.INSTRUCTOR_ID,
            r.GROSS_AMOUNT,
            r.PAYMENT_FEE_AMOUNT,
            r.PLATFORM_FEE_AMOUNT,
            r.INSTRUCTOR_AMOUNT,
            r.REVENUE_SHARE_PERCENT,
            r.PERIOD_KEY,
            r.STATUS,
            r.CreatedAtUtc);
}
