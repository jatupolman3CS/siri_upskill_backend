using Siri.Modules.Commerce.Domain;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Payout.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// No <c>IRefundService</c> interface — same reasoning as <c>OrderService</c>'s own doc comment.
/// Backs the finance-report mockup's request/approve/reject-refund workflow (docs/DECISIONS.md D-17's
/// gap-fill — see <see cref="REFUND"/>'s own doc comment).
/// <para>
/// <b>Live-attendance hard block (P11-12, Q13.4 — docs/contracts/P11-12-refund-hard-block-after-live-join.md):</b> once the buyer has been handed the room link
/// of a live session of a course in the order, the system refuses (409) to take a refund request for that course's share — it never reaches the admin queue.
/// <see cref="ComputeLiveAttendanceCeilingAsync"/> works the ceiling out. <see cref="ApproveAsync"/>/<see cref="RejectAsync"/> are deliberately untouched:
/// a request filed before the learner entered a room and approved by an admin afterwards is the admin's decision. <paramref name="liveAttendance"/> is optional so a
/// host that does not load the Live module behaves exactly as before.
/// </para>
/// </summary>
public sealed class RefundService(
    IRefundRepository refundRepository,
    IPaymentRepository paymentRepository,
    IOrderRepository orderRepository,
    IPromoCodeRepository promoCodeRepository,
    IClock clock,
    IRevenueSplitContract? revenueSplitContract = null,
    ILiveAttendanceReader? liveAttendance = null)
{
    private const string LiveAttendedMessage = "คุณเข้าร่วมคาบสอนสดของคอร์สในรายการนี้แล้ว จึงขอคืนเงินส่วนของคอร์สนั้นไม่ได้";

    /// <summary>What the live-attendance rule allows for one payment: the most that can still be refunded, the part that is blocked, and the blocked courses' titles.</summary>
    private readonly record struct LiveAttendanceCeiling(decimal MaxRefundable, decimal BlockedAmount, IReadOnlyList<string> BlockedCourseTitles);

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

        // P11-12: after the format checks, before anything is created — a learner who has been in a live room cannot get that course's share back.
        var ceiling = await ComputeLiveAttendanceCeilingAsync(order, payment, cancellationToken).ConfigureAwait(false);
        if (ceiling is { } limit && command.Amount > limit.MaxRefundable)
        {
            return Result.Failure<RefundResponse>(LiveAttendedError(limit));
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
                if (order is not null)
                {
                    if (order.PROMO_CODE_ID.HasValue)
                    {
                        await promoCodeRepository.RevertRedemptionAsync(order.PROMO_CODE_ID.Value, order.ORDER_ID, cancellationToken).ConfigureAwait(false);
                    }

                    if (revenueSplitContract is not null)
                    {
                        var orderItemIds = order.ORDER_ITEMS.Select(i => i.ORDER_ITEM_ID).ToList();
                        await revenueSplitContract.ReverseRevenueSplitsForOrderAsync(order.ORDER_ID, orderItemIds, cancellationToken).ConfigureAwait(false);
                    }
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

    /// <summary>
    /// The live-attendance ceiling of one payment (contract section 1), or <c>null</c> when nothing is blocked: no Live module in this host, no course lines in
    /// the order, or the buyer has never been handed a room link for any of them. Which courses count is asked of Live in <b>one batch call, always with the
    /// order owner's id</b> (never an id from the request); only <em>learner</em> joins count there, so an instructor entering their own room never blocks anyone.
    /// <code>
    /// blockedLine   = sum of LINE_TOTAL of the lines whose course the buyer attended
    /// totalLine     = sum of LINE_TOTAL of every line
    /// blockedAmount = totalLine &gt; 0 ? Round(payment.AMOUNT * blockedLine / totalLine, 2, AwayFromZero) : payment.AMOUNT
    /// maxRefundable = Max(0, payment.AMOUNT - blockedAmount)
    /// </code>
    /// The payment's real amount is split in proportion to the line totals (not the list prices: <c>LINE_TOTAL</c> is already after any per-line discount), so a
    /// promo code or a rounding difference between the lines and the charge cannot make the ceiling exceed what was paid. A line without a course (a bundle
    /// line) is never blocked by itself but still counts in <c>totalLine</c>.
    /// </summary>
    private async Task<LiveAttendanceCeiling?> ComputeLiveAttendanceCeilingAsync(ORDER order, PAYMENT payment, CancellationToken cancellationToken)
    {
        if (liveAttendance is null)
        {
            return null;
        }

        var courseIds = order.ORDER_ITEMS
            .Where(item => item.COURSE_ID.HasValue)
            .Select(item => item.COURSE_ID!.Value)
            .Distinct()
            .ToArray();

        if (courseIds.Length == 0)
        {
            return null;
        }

        var attended = await liveAttendance.GetCourseIdsAttendedAsync(order.USER_ID, courseIds, cancellationToken).ConfigureAwait(false);
        if (attended.Count == 0)
        {
            return null;
        }

        var blockedItems = order.ORDER_ITEMS
            .Where(item => item.COURSE_ID is { } courseId && attended.Contains(courseId))
            .ToList();

        // Defensive: the reader only answers with ids it was asked about, so this is "cannot happen" — but a blocked set with no line behind it must not block.
        if (blockedItems.Count == 0)
        {
            return null;
        }

        var totalLine = order.ORDER_ITEMS.Sum(item => item.LINE_TOTAL);
        var blockedLine = blockedItems.Sum(item => item.LINE_TOTAL);

        var blockedAmount = totalLine > 0m
            ? decimal.Round(payment.AMOUNT * blockedLine / totalLine, 2, MidpointRounding.AwayFromZero)
            : payment.AMOUNT;

        var maxRefundable = Math.Max(0m, payment.AMOUNT - blockedAmount);

        // Keep the order the lines are held in (the order they were added to the order). Do NOT sort by ORDER_ITEM_ID: UUIDv7 ids created in the
        // same millisecond (every line of one order is) are not ordered relative to each other, so that sort would shuffle the titles at random.
        var titles = blockedItems
            .Select(item => item.TITLE_SNAPSHOT)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new LiveAttendanceCeiling(maxRefundable, blockedAmount, titles);
    }

    /// <summary>409 <c>refund.live_attended</c>. Carries only the buyer's own course titles and two amounts — never a session id, a join time or an instructor's name.</summary>
    private static DomainError LiveAttendedError(LiveAttendanceCeiling limit) =>
        DomainError.Conflict(LiveAttendedMessage).WithReason(
            RefundReasons.LiveAttended,
            new Dictionary<string, object?>
            {
                ["blockedCourseTitles"] = limit.BlockedCourseTitles,
                ["maxRefundableAmount"] = limit.MaxRefundable,
                ["blockedAmount"] = limit.BlockedAmount,
            });

    private static RefundResponse ToResponse(REFUND refund) =>
        new(refund.REFUND_ID, refund.PAYMENT_ID, refund.AMOUNT, refund.REASON, refund.STATUS,
            refund.REQUESTED_AT_UTC, refund.DECIDED_AT_UTC, refund.DECISION_NOTE, refund.COMPLETED_AT_UTC);
}

/// <summary>Stable <c>reason</c> sub-codes of the refund endpoints (appendix section D) — the front end maps them to i18n messages.</summary>
public static class RefundReasons
{
    /// <summary>The buyer has been in a live room of a course in the order (P11-12, Q13.4).</summary>
    public const string LiveAttended = "refund.live_attended";
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
