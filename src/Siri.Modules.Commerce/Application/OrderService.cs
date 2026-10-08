using Siri.Integrations.Payment;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Learning.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>Order application service managing customer orders and lifecycle.</summary>
public sealed class OrderService(
    IOrderRepository orderRepository,
    IPromoCodeRepository promoCodeRepository,
    ICatalogPriceContract catalogPriceContract,
    ILiveScheduleReader liveScheduleReader,
    ILearningAccessContract learningAccessContract,
    IPricingEngine pricingEngine,
    IClock clock,
    IPaymentRepository paymentRepository,
    IPaymentMethod paymentMethod)
{
    public async Task<Result<OrderResponse>> GetByIdAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<OrderResponse>(DomainError.NotFound("ไม่พบคำสั่งซื้อที่ระบุ"));
        }

        var paymentIds = await paymentRepository.GetSucceededPaymentIdsByOrderIdsAsync([order.ORDER_ID], cancellationToken).ConfigureAwait(false);
        return Result.Success(ToResponse(order, paymentIds.TryGetValue(order.ORDER_ID, out var paymentId) ? paymentId : null));
    }

    public async Task<Result<OrderResponse>> CreateAsync(Guid userId, CreateOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CourseIds is null || command.CourseIds.Count == 0)
        {
            return Result.Failure<OrderResponse>(DomainError.Validation("กรุณาระบุคอร์สเรียนที่ต้องการสั่งซื้อ"));
        }

        // Check duplicate active enrollment — one batched query across the whole course set instead of
        // looping HasActiveEnrollmentAsync per course.
        var alreadyActiveCourseIds = await learningAccessContract.HasActiveEnrollmentsAsync(userId, command.CourseIds, cancellationToken).ConfigureAwait(false);
        if (alreadyActiveCourseIds.Count > 0)
        {
            var firstAlreadyEnrolledCourseId = command.CourseIds.First(alreadyActiveCourseIds.Contains);
            return Result.Failure<OrderResponse>(DomainError.Conflict($"คุณได้ลงทะเบียนเรียนคอร์สนี้แล้ว ({firstAlreadyEnrolledCourseId})"));
        }

        // P11-11 (Q13.1): enrollment deadline check, before pricing — a course that has closed
        // enrollment shouldn't even get as far as calculating a price for it.
        var enrollmentPolicies = await catalogPriceContract.GetEnrollmentPoliciesAsync(command.CourseIds, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;

        foreach (var courseId in command.CourseIds.Distinct())
        {
            if (enrollmentPolicies.TryGetValue(courseId, out var policy) &&
                policy.EnrollmentDeadlineUtc is { } deadline && now > deadline)
            {
                return Result.Failure<OrderResponse>(DomainError.Conflict("ปิดรับสมัครคอร์สนี้แล้ว"));
            }
        }

        // Every course on the order reserves a seat inside the order-creation transaction (§4.2), capped or
        // not (TryReserveSeatAsync always succeeds when MaxSeats is null). Reserve and the release paths
        // (CancelAsync / OrderExpiryJob, which release for every course unconditionally) must stay
        // symmetric, otherwise SeatsUsed drifts low when a cap is set or cleared while orders are open.
        var seatCourseIds = command.CourseIds.Distinct().ToList();

        var pricingResult = await pricingEngine.CalculatePricingAsync(
            new PricingCalculationRequest(userId, command.CourseIds, command.BundleId, command.PromoCode),
            cancellationToken).ConfigureAwait(false);

        if (pricingResult.IsFailure)
        {
            return Result.Failure<OrderResponse>(pricingResult.Error);
        }

        var pricing = pricingResult.Value;
        var totalDiscount = (pricing.OriginalSubtotal - pricing.SubtotalAfterItemDiscounts) + pricing.PromoDiscount;

        var randomSuffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var orderNo = $"SU-{clock.UtcNow:yyMMdd}-{randomSuffix}";

        var order = ORDER.Create(orderNo, userId, pricing.OriginalSubtotal, totalDiscount, pricing.TaxAmount, pricing.TotalAmount, pricing.AppliedPromoCode?.PROMO_CODE_ID);
        foreach (var item in pricing.Items)
        {
            order.AddItem(item.CourseId, item.Title, item.OriginalBaselinePrice, item.FinalLineTotal);
        }

        return await orderRepository.ExecuteInTransactionAsync(async () =>
        {
            // P11-11 (Q13.2): atomic seat reservation, inside the same transaction as order creation —
            // if a later course in the same order fails to reserve a seat, ExecuteInTransactionAsync's
            // rollback-on-failure (see its own doc comment) undoes any earlier successful reservation
            // too, so there's no compensating-undo to write here.
            foreach (var courseId in seatCourseIds)
            {
                var reserved = await catalogPriceContract.TryReserveSeatAsync(courseId, cancellationToken).ConfigureAwait(false);
                if (!reserved)
                {
                    return Result.Failure<OrderResponse>(DomainError.Conflict("คอร์สนี้ที่นั่งเต็มแล้ว"));
                }
            }

            await orderRepository.AddAsync(order, cancellationToken).ConfigureAwait(false);

            if (pricing.AppliedPromoCode is not null)
            {
                var redeemed = await promoCodeRepository.TryRedeemAsync(
                    pricing.AppliedPromoCode.PROMO_CODE_ID, order.ORDER_ID, userId, pricing.AppliedPromoCode.MAX_PER_USER, clock, cancellationToken).ConfigureAwait(false);
                if (!redeemed)
                {
                    return Result.Failure<OrderResponse>(DomainError.Conflict("โค้ดส่วนลดถูกใช้งานจนครบโควตาหรือสิทธิ์ต่อผู้ใช้แล้ว"));
                }
            }

            // If total is 0 (e.g. 100% discount promo code), mark paid immediately and enroll
            if (pricing.TotalAmount == 0m)
            {
                order.MarkAwaitingPayment();
                order.MarkPaid(clock);
                await orderRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var coursePrices = await catalogPriceContract.GetPublishedCoursePricesAsync(command.CourseIds, cancellationToken).ConfigureAwait(false);

                // One batched enroll call (one query to load existing enrollments for this course set,
                // one SaveChangesAsync) instead of looping EnrollUserAsync per course.
                //
                // P11-13 (Q13.3): Live/Hybrid courses count AccessDurationDays from the first scheduled
                // live session's StartsAtUtc, not the purchase date — async per course, so this can no
                // longer be a plain LINQ .Select(). GetEarliestScheduledSessionAsync returning null covers
                // both "course is OnDemand" and "Live/Hybrid but no session scheduled yet" — both fall back
                // to counting from now, exactly the pre-P11-13 behavior (see
                // docs/contracts/P11-13-access-duration-first-session.md §0.2).
                var enrollmentGrants = new List<CourseEnrollmentGrant>(command.CourseIds.Count);
                foreach (var courseId in command.CourseIds)
                {
                    DateTime? expiresAt = null;
                    if (coursePrices.TryGetValue(courseId, out var courseInfo) && courseInfo.AccessDurationDays is { } days)
                    {
                        var earliestSession = await liveScheduleReader.GetEarliestScheduledSessionAsync(courseId, cancellationToken).ConfigureAwait(false);
                        // Never count from a session that has already started (late buyers would lose the
                        // elapsed time, or get an already-expired enrollment).
                        var accessStartUtc = earliestSession is not null && earliestSession.StartsAtUtc > clock.UtcNow
                            ? earliestSession.StartsAtUtc
                            : clock.UtcNow;
                        expiresAt = accessStartUtc.AddDays(days);
                    }
                    enrollmentGrants.Add(new CourseEnrollmentGrant(courseId, order.ORDER_ID, expiresAt));
                }

                var enrollmentResult = await learningAccessContract.EnrollUserInCoursesAsync(
                    userId, "PromoCode_100", enrollmentGrants, cancellationToken).ConfigureAwait(false);
                if (enrollmentResult.IsFailure)
                {
                    return Result.Failure<OrderResponse>(enrollmentResult.Error);
                }
            }

            return Result.Success(ToResponse(order));
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<OrderResponse>> CancelAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<OrderResponse>(DomainError.NotFound("ไม่พบคำสั่งซื้อที่ระบุ"));
        }

        // P11-11 §4.3: this used to mutate + SaveChangesAsync with no surrounding transaction at all
        // (unlike CreateAsync/OrderExpiryJob) — a pre-existing gap, now closed because seat release must
        // be atomic with MarkCancelled/RevertRedemptionAsync (database.md: multi-table changes that must
        // be atomic belong in one transaction).
        return await orderRepository.ExecuteInTransactionAsync(async () =>
        {
            try
            {
                order.MarkCancelled();
            }
            catch (InvalidOperationException ex)
            {
                return Result.Failure<OrderResponse>(DomainError.Conflict(ex.Message));
            }

            if (order.PROMO_CODE_ID.HasValue)
            {
                await promoCodeRepository.RevertRedemptionAsync(order.PROMO_CODE_ID.Value, order.ORDER_ID, cancellationToken).ConfigureAwait(false);
            }

            // Cancel any still-open PaymentIntent (PromptPay QR / card) so it cannot be paid after the order
            // is cancelled and its seat handed to someone else — same handling as OrderExpiryJob. A failed
            // Stripe cancel (e.g. the intent just succeeded) aborts the whole cancel and rolls back.
            var pendingPayments = await paymentRepository.GetPendingByOrderIdAsync(order.ORDER_ID, cancellationToken).ConfigureAwait(false);
            foreach (var payment in pendingPayments)
            {
                if (!string.IsNullOrWhiteSpace(payment.PROVIDER_PAYMENT_INTENT_ID))
                {
                    var cancelResult = await paymentMethod.CancelPaymentIntentAsync(payment.PROVIDER_PAYMENT_INTENT_ID, cancellationToken).ConfigureAwait(false);
                    if (cancelResult.IsFailure)
                    {
                        return Result.Failure<OrderResponse>(DomainError.Conflict("ไม่สามารถยกเลิกการชำระเงินที่ค้างอยู่ได้ กรุณาลองใหม่อีกครั้ง"));
                    }
                }

                payment.MarkExpired();
            }

            // Unconditional, symmetric with CreateAsync (which reserves a seat for every course on the
            // order); ReleaseSeatAsync's WHERE SeatsUsed > 0 keeps the counter from going negative.
            foreach (var courseId in order.ORDER_ITEMS.Select(i => i.COURSE_ID).Where(id => id.HasValue).Select(id => id!.Value).Distinct())
            {
                await catalogPriceContract.ReleaseSeatAsync(courseId, cancellationToken).ConfigureAwait(false);
            }

            await orderRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(ToResponse(order));
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<OrderResponse>> ListUserOrdersAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var (items, totalCount) = await orderRepository.ListByUserIdAsync(userId, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);

        // One batched lookup for the whole page (never one per order): the successful payment each order's refund request would refer to.
        var paymentIds = await paymentRepository
            .GetSucceededPaymentIdsByOrderIdsAsync(items.Select(o => o.ORDER_ID).ToArray(), cancellationToken)
            .ConfigureAwait(false);

        var mapped = items.Select(o => ToResponse(o, paymentIds.TryGetValue(o.ORDER_ID, out var paymentId) ? paymentId : null)).ToList();
        return PagedResult<OrderResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    private static OrderResponse ToResponse(ORDER order, Guid? paymentId = null) =>
        new(
            order.ORDER_ID,
            order.ORDER_NO,
            order.SUBTOTAL_AMOUNT,
            order.DISCOUNT_AMOUNT,
            order.TAX_AMOUNT,
            order.TOTAL_AMOUNT,
            order.CURRENCY,
            order.STATUS,
            order.CreatedAtUtc,
            order.PAID_AT_UTC,
            order.ORDER_ITEMS.Select(i => new OrderItemResponse(i.ORDER_ITEM_ID, i.COURSE_ID, i.TITLE_SNAPSHOT, i.UNIT_PRICE, i.LINE_TOTAL)).ToList(),
            paymentId);
}

public sealed record OrderItemResponse(
    Guid Id,
    Guid? CourseId,
    string TitleSnapshot,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderResponse(
    Guid Id,
    string OrderNo,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Currency,
    OrderStatus Status,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    IReadOnlyList<OrderItemResponse> Items,
    Guid? PaymentId = null);

public sealed record CreateOrderCommand(IReadOnlyList<Guid> CourseIds, string? PromoCode = null, Guid? BundleId = null);
