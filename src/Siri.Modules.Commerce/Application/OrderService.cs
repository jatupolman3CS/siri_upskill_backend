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

        // Check duplicate active enrollment
        foreach (var courseId in command.CourseIds)
        {
            var alreadyEnrolled = await learningAccessContract.HasActiveEnrollmentAsync(userId, courseId, cancellationToken).ConfigureAwait(false);
            if (alreadyEnrolled)
            {
                return Result.Failure<OrderResponse>(DomainError.Conflict($"คุณได้ลงทะเบียนเรียนคอร์สนี้แล้ว ({courseId})"));
            }
        }

        // Server-side price calculation from Catalog contract
        var coursePrices = await catalogPriceContract.GetPublishedCoursePricesAsync(command.CourseIds, cancellationToken).ConfigureAwait(false);
        if (coursePrices.Count != command.CourseIds.Distinct().Count())
        {
            return Result.Failure<OrderResponse>(DomainError.NotFound("พบคอร์สเรียนบางรายการที่ไม่มีอยู่หรือยังไม่เปิดจำหน่าย"));
        }

        var subtotal = coursePrices.Values.Sum(c => c.Price);
        var discount = 0m;
        PROMO_CODE? appliedPromo = null;

        if (!string.IsNullOrWhiteSpace(command.PromoCode))
        {
            var normalizedCode = command.PromoCode.Trim().ToUpperInvariant();
            var promo = await promoCodeRepository.GetByCodeAsync(normalizedCode, cancellationToken).ConfigureAwait(false);
            if (promo is null || !promo.IS_ACTIVE)
            {
                return Result.Failure<OrderResponse>(DomainError.Validation("โค้ดส่วนลดไม่ถูกต้องหรือถูกปิดใช้งาน"));
            }

            var now = clock.UtcNow;
            if (now < promo.STARTS_AT_UTC || now > promo.ENDS_AT_UTC)
            {
                return Result.Failure<OrderResponse>(DomainError.Validation("โค้ดส่วนลดหมดอายุหรือไม่สามารถใช้งานได้ในขณะนี้"));
            }

            if (promo.REDEEMED_COUNT >= promo.MAX_REDEMPTIONS)
            {
                return Result.Failure<OrderResponse>(DomainError.Conflict("โค้ดส่วนลดถูกใช้งานจนครบโควตาแล้ว"));
            }

            var userRedemptions = await promoCodeRepository.GetUserRedemptionCountAsync(promo.PROMO_CODE_ID, userId, cancellationToken).ConfigureAwait(false);
            if (userRedemptions >= promo.MAX_PER_USER)
            {
                return Result.Failure<OrderResponse>(DomainError.Validation("คุณได้ใช้โค้ดส่วนลดนี้ครบตามจำนวนสิทธิ์ที่กำหนดแล้ว"));
            }

            decimal applicableSubtotal;
            switch (promo.SCOPE)
            {
                case PromoCodeScope.AllCourses:
                    applicableSubtotal = subtotal;
                    break;
                case PromoCodeScope.Course:
                    applicableSubtotal = promo.SCOPE_REF_ID.HasValue && command.CourseIds.Contains(promo.SCOPE_REF_ID.Value) && coursePrices.TryGetValue(promo.SCOPE_REF_ID.Value, out var coursePrice)
                        ? coursePrice.Price
                        : 0m;
                    if (applicableSubtotal == 0m)
                    {
                        return Result.Failure<OrderResponse>(DomainError.Validation("โค้ดส่วนลดนี้ใช้ได้เฉพาะคอร์สที่กำหนด"));
                    }
                    break;
                default:
                    applicableSubtotal = subtotal;
                    break;
            }

            if (applicableSubtotal < promo.MIN_ORDER_AMOUNT)
            {
                return Result.Failure<OrderResponse>(DomainError.Validation($"ยอดสั่งซื้อขั้นต่ำสำหรับโค้ดนี้คือ ฿{promo.MIN_ORDER_AMOUNT:N0}"));
            }

            discount = promo.DISCOUNT_TYPE switch
            {
                PromoCodeDiscountType.Fixed => Math.Min(applicableSubtotal, promo.DISCOUNT_VALUE),
                PromoCodeDiscountType.Percentage => Math.Round(applicableSubtotal * (promo.DISCOUNT_VALUE / 100m), 2),
                _ => 0m,
            };

            appliedPromo = promo;
        }

        var total = Math.Max(0m, subtotal - discount);
        var tax = Math.Round(total * 7m / 107m, 2);

        var randomSuffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var orderNo = $"SU-{clock.UtcNow:yyMMdd}-{randomSuffix}";

        var order = ORDER.Create(orderNo, userId, subtotal, discount, tax, total, appliedPromo?.PROMO_CODE_ID);
        foreach (var courseId in command.CourseIds)
        {
            var courseInfo = coursePrices[courseId];
            decimal lineTotal;
            if (appliedPromo is not null && appliedPromo.SCOPE == PromoCodeScope.Course && appliedPromo.SCOPE_REF_ID == courseId)
            {
                lineTotal = Math.Max(0m, courseInfo.Price - discount);
            }
            else if (subtotal > 0 && discount > 0)
            {
                var proportion = courseInfo.Price / subtotal;
                lineTotal = Math.Max(0m, Math.Round(courseInfo.Price - (discount * proportion), 2));
            }
            else
            {
                lineTotal = courseInfo.Price;
            }

            order.AddItem(courseInfo.CourseId, courseInfo.Title, courseInfo.Price, lineTotal);
        }

        return await orderRepository.ExecuteInTransactionAsync(async () =>
        {
            await orderRepository.AddAsync(order, cancellationToken).ConfigureAwait(false);

            if (appliedPromo is not null)
            {
                var redeemed = await promoCodeRepository.TryRedeemAsync(
                    appliedPromo.PROMO_CODE_ID, order.ORDER_ID, userId, appliedPromo.MAX_PER_USER, clock, cancellationToken).ConfigureAwait(false);
                if (!redeemed)
                {
                    return Result.Failure<OrderResponse>(DomainError.Conflict("โค้ดส่วนลดถูกใช้งานจนครบโควตาหรือสิทธิ์ต่อผู้ใช้แล้ว"));
                }
            }

            // If total is 0 (e.g. 100% discount promo code), mark paid immediately and enroll
            if (total == 0m)
            {
                order.MarkAwaitingPayment();
                order.MarkPaid(clock);
                await orderRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                foreach (var courseId in command.CourseIds)
                {
                    var courseInfo = coursePrices[courseId];
                    DateTime? expiresAt = courseInfo.AccessDurationDays.HasValue
                        ? clock.UtcNow.AddDays(courseInfo.AccessDurationDays.Value)
                        : null;

                    await learningAccessContract.EnrollUserAsync(
                        userId,
                        courseId,
                        order.ORDER_ID,
                        "PromoCode_100",
                        expiresAt,
                        cancellationToken).ConfigureAwait(false);
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

    private static OrderResponse ToResponse(ORDER order) =>
        new(order.ORDER_ID, order.ORDER_NO, order.TOTAL_AMOUNT, order.CURRENCY, order.STATUS);
}

public sealed record OrderResponse(Guid Id, string OrderNo, decimal TotalAmount, string Currency, OrderStatus Status);

public sealed record CreateOrderCommand(IReadOnlyList<Guid> CourseIds, string? PromoCode = null);
