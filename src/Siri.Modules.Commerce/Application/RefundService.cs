using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// No <c>IRefundService</c> interface — same reasoning as <c>OrderService</c>'s own doc comment.
/// Backs the finance-report mockup's request/approve/reject-refund workflow (docs/DECISIONS.md D-17's
/// gap-fill — see <see cref="REFUND"/>'s own doc comment).
/// </summary>
public sealed class RefundService(
    IRefundRepository refundRepository,
    IPaymentRepository paymentRepository,
    IOrderRepository orderRepository,
    IPromoCodeRepository promoCodeRepository,
    IClock clock)
{
    public async Task<Result<RefundResponse>> RequestAsync(Guid userId, RequestRefundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payment = await paymentRepository.GetByIdAsync(command.PaymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result.Failure<RefundResponse>(DomainError.NotFound("ไม่พบข้อมูลการชำระเงินที่ระบุ"));
        }

        var order = await orderRepository.GetByIdAsync(payment.ORDER_ID, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<RefundResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์ขอคืนเงินสำหรับรายการนี้"));
        }

        if (payment.STATUS != PaymentStatus.Succeeded)
        {
            return Result.Failure<RefundResponse>(DomainError.Validation("สามารถขอคืนเงินได้เฉพาะรายการที่ชำระเงินสำเร็จแล้วเท่านั้น"));
        }

        if (command.Amount <= 0 || command.Amount > payment.AMOUNT)
        {
            return Result.Failure<RefundResponse>(DomainError.Validation("จำนวนเงินที่ขอคืนต้องมากกว่า 0 และไม่เกินยอดที่ชำระจริง"));
        }

        var refund = REFUND.Request(command.PaymentId, command.Amount, command.Reason, userId, clock);
        await refundRepository.AddAsync(refund, cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(refund));
    }

    public async Task<Result<RefundResponse>> GetByIdAsync(Guid userId, Guid refundId, CancellationToken cancellationToken)
    {
        var refund = await refundRepository.GetByIdAsync(refundId, cancellationToken).ConfigureAwait(false);
        if (refund is null || refund.REQUESTED_BY_USER_ID != userId)
        {
            return Result.Failure<RefundResponse>(DomainError.NotFound("ไม่พบคำขอคืนเงินที่ระบุ"));
        }

        return Result.Success(ToResponse(refund));
    }

    public async Task<PagedResult<RefundResponse>> ListPendingAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

        var (items, totalCount) = await refundRepository.ListPendingPagedAsync(
            effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);

        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<RefundResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    public async Task<Result<RefundResponse>> ApproveAsync(Guid decidedByUserId, Guid refundId, ApproveRefundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var refund = await refundRepository.GetByIdAsync(refundId, cancellationToken).ConfigureAwait(false);
        if (refund is null)
        {
            return Result.Failure<RefundResponse>(DomainError.NotFound("ไม่พบคำขอคืนเงินที่ระบุ"));
        }

        try
        {
            refund.Approve(decidedByUserId, command.DecisionNote, clock);

            var payment = await paymentRepository.GetByIdAsync(refund.PAYMENT_ID, cancellationToken).ConfigureAwait(false);
            if (payment is not null)
            {
                var order = await orderRepository.GetByIdAsync(payment.ORDER_ID, cancellationToken).ConfigureAwait(false);
                if (order is not null && order.PROMO_CODE_ID.HasValue)
                {
                    await promoCodeRepository.RevertRedemptionAsync(order.PROMO_CODE_ID.Value, order.ORDER_ID, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<RefundResponse>(DomainError.Conflict(ex.Message));
        }

        await refundRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(ToResponse(refund));
    }

    public async Task<Result<RefundResponse>> RejectAsync(Guid decidedByUserId, Guid refundId, RejectRefundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var refund = await refundRepository.GetByIdAsync(refundId, cancellationToken).ConfigureAwait(false);
        if (refund is null)
        {
            return Result.Failure<RefundResponse>(DomainError.NotFound("ไม่พบคำขอคืนเงินที่ระบุ"));
        }

        try
        {
            refund.Reject(decidedByUserId, command.DecisionNote, clock);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<RefundResponse>(DomainError.Conflict(ex.Message));
        }

        await refundRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(ToResponse(refund));
    }

    private static RefundResponse ToResponse(REFUND refund) =>
        new(refund.REFUND_ID, refund.PAYMENT_ID, refund.AMOUNT, refund.REASON, refund.STATUS,
            refund.REQUESTED_AT_UTC, refund.DECIDED_AT_UTC, refund.DECISION_NOTE, refund.COMPLETED_AT_UTC);
}

public sealed record RefundResponse(
    Guid Id,
    Guid PaymentId,
    decimal Amount,
    string Reason,
    RefundStatus Status,
    DateTime RequestedAtUtc,
    DateTime? DecidedAtUtc,
    string? DecisionNote,
    DateTime? CompletedAtUtc);

public sealed record RequestRefundCommand(Guid PaymentId, decimal Amount, string Reason);

public sealed record ApproveRefundCommand(string? DecisionNote);

public sealed record RejectRefundCommand(string DecisionNote);
