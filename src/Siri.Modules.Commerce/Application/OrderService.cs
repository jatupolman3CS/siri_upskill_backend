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
    ILearningAccessContract learningAccessContract,
    IPricingEngine pricingEngine,
    IClock clock)
{
    public async Task<Result<OrderResponse>> GetByIdAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<OrderResponse>(DomainError.NotFound("ไม่พบคำสั่งซื้อที่ระบุ"));
        }

        return Result.Success(ToResponse(order));
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
                var enrollmentGrants = command.CourseIds
                    .Select(courseId =>
                    {
                        coursePrices.TryGetValue(courseId, out var courseInfo);
                        DateTime? expiresAt = courseInfo?.AccessDurationDays.HasValue == true
                            ? clock.UtcNow.AddDays(courseInfo.AccessDurationDays.Value)
                            : null;
                        return new CourseEnrollmentGrant(courseId, order.ORDER_ID, expiresAt);
                    })
                    .ToList();

                await learningAccessContract.EnrollUserInCoursesAsync(
                    userId, "PromoCode_100", enrollmentGrants, cancellationToken).ConfigureAwait(false);
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

        try
        {
            order.MarkCancelled();
            if (order.PROMO_CODE_ID.HasValue)
            {
                await promoCodeRepository.RevertRedemptionAsync(order.PROMO_CODE_ID.Value, order.ORDER_ID, cancellationToken).ConfigureAwait(false);
            }
            await orderRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<OrderResponse>(DomainError.Conflict(ex.Message));
        }

        return Result.Success(ToResponse(order));
    }

    public async Task<PagedResult<OrderResponse>> ListUserOrdersAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var (items, totalCount) = await orderRepository.ListByUserIdAsync(userId, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<OrderResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    private static OrderResponse ToResponse(ORDER order) =>
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
            order.ORDER_ITEMS.Select(i => new OrderItemResponse(i.ORDER_ITEM_ID, i.COURSE_ID, i.TITLE_SNAPSHOT, i.UNIT_PRICE, i.LINE_TOTAL)).ToList());
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
    IReadOnlyList<OrderItemResponse> Items);

public sealed record CreateOrderCommand(IReadOnlyList<Guid> CourseIds, string? PromoCode = null, Guid? BundleId = null);
