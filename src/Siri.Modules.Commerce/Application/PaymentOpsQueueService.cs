using Microsoft.Extensions.Logging;
using Siri.Integrations.Payment;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Payout.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

public sealed class PaymentOpsQueueService
{
    private readonly IPaymentOpsQueueRepository _opsQueueRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IPromoCodeRepository _promoCodeRepository;
    private readonly IPaymentMethod _paymentMethod;
    private readonly ILearningAccessContract _learningAccessContract;
    private readonly ICatalogPriceContract _catalogPriceContract;
    private readonly IRevenueSplitContract? _revenueSplitContract;
    private readonly IEmailOutbox _emailOutbox;
    private readonly IUserContactReader _userContactReader;
    private readonly IClock _clock;
    private readonly ILogger<PaymentOpsQueueService> _logger;

    public PaymentOpsQueueService(
        IPaymentOpsQueueRepository opsQueueRepository,
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        IPromoCodeRepository promoCodeRepository,
        IPaymentMethod paymentMethod,
        ILearningAccessContract learningAccessContract,
        ICatalogPriceContract catalogPriceContract,
        IEmailOutbox emailOutbox,
        IUserContactReader userContactReader,
        IClock clock,
        ILogger<PaymentOpsQueueService> logger,
        IRevenueSplitContract? revenueSplitContract = null)
    {
        _opsQueueRepository = opsQueueRepository;
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _promoCodeRepository = promoCodeRepository;
        _paymentMethod = paymentMethod;
        _learningAccessContract = learningAccessContract;
        _catalogPriceContract = catalogPriceContract;
        _emailOutbox = emailOutbox;
        _userContactReader = userContactReader;
        _clock = clock;
        _logger = logger;
        _revenueSplitContract = revenueSplitContract;
    }

    public async Task<PagedResult<PaymentOpsQueueDto>> ListAsync(
        PaymentOpsQueueStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

        var (items, totalCount) = await _opsQueueRepository.ListAsync(
            status, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);

        var dtoList = new List<PaymentOpsQueueDto>(items.Count);
        foreach (var item in items)
        {
            var payment = await _paymentRepository.GetByIdAsync(item.PAYMENT_ID, cancellationToken).ConfigureAwait(false);
            ORDER? order = null;
            string? userEmail = null;

            if (payment is not null)
            {
                order = await _orderRepository.GetByIdAsync(payment.ORDER_ID, cancellationToken).ConfigureAwait(false);
                if (order is not null)
                {
                    userEmail = await _userContactReader.GetEmailAsync(order.USER_ID, cancellationToken).ConfigureAwait(false);
                }
            }

            dtoList.Add(new PaymentOpsQueueDto(
                item.PAYMENT_OPS_QUEUE_ID,
                item.PAYMENT_ID,
                item.REASON,
                item.STATUS,
                item.ASSIGNED_TO_USER_ID,
                item.RESOLVED_BY_USER_ID,
                item.RESOLVED_AT_UTC,
                item.NOTE,
                order?.ORDER_ID,
                order?.ORDER_NO,
                order?.USER_ID,
                userEmail,
                payment?.AMOUNT ?? 0m,
                payment?.METHOD ?? PaymentMethod.PromptPay,
                payment?.STATUS ?? PaymentStatus.Pending,
                order?.STATUS ?? OrderStatus.AwaitingPayment,
                payment?.PROVIDER_PAYMENT_INTENT_ID));
        }

        return PagedResult<PaymentOpsQueueDto>.Create(dtoList, totalCount, effectivePage, effectivePageSize);
    }

    public async Task<Result<PaymentOpsQueueDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await _opsQueueRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<PaymentOpsQueueDto>(DomainError.NotFound("ไม่พบรายการในคิวตรวจสอบการชำระเงินที่ระบุ"));
        }

        var payment = await _paymentRepository.GetByIdAsync(item.PAYMENT_ID, cancellationToken).ConfigureAwait(false);
        ORDER? order = null;
        string? userEmail = null;

        if (payment is not null)
        {
            order = await _orderRepository.GetByIdAsync(payment.ORDER_ID, cancellationToken).ConfigureAwait(false);
            if (order is not null)
            {
                userEmail = await _userContactReader.GetEmailAsync(order.USER_ID, cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success(new PaymentOpsQueueDto(
            item.PAYMENT_OPS_QUEUE_ID,
            item.PAYMENT_ID,
            item.REASON,
            item.STATUS,
            item.ASSIGNED_TO_USER_ID,
            item.RESOLVED_BY_USER_ID,
            item.RESOLVED_AT_UTC,
            item.NOTE,
            order?.ORDER_ID,
            order?.ORDER_NO,
            order?.USER_ID,
            userEmail,
            payment?.AMOUNT ?? 0m,
            payment?.METHOD ?? PaymentMethod.PromptPay,
            payment?.STATUS ?? PaymentStatus.Pending,
            order?.STATUS ?? OrderStatus.AwaitingPayment,
            payment?.PROVIDER_PAYMENT_INTENT_ID));
    }

    public async Task<Result<PaymentOpsQueueDto>> AssignAsync(
        Guid id,
        Guid adminUserId,
        CancellationToken cancellationToken)
    {
        var item = await _opsQueueRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<PaymentOpsQueueDto>(DomainError.NotFound("ไม่พบรายการในคิวตรวจสอบการชำระเงินที่ระบุ"));
        }

        try
        {
            item.Assign(adminUserId);
            await _opsQueueRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<PaymentOpsQueueDto>(DomainError.Conflict(ex.Message));
        }
    }

    public async Task<Result<PaymentOpsQueueDto>> ResolveAsync(
        Guid id,
        Guid adminUserId,
        ResolvePaymentOpsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var item = await _opsQueueRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<PaymentOpsQueueDto>(DomainError.NotFound("ไม่พบรายการในคิวตรวจสอบการชำระเงินที่ระบุ"));
        }

        var payment = await _paymentRepository.GetByIdAsync(item.PAYMENT_ID, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result.Failure<PaymentOpsQueueDto>(DomainError.NotFound("ไม่พบข้อมูลการชำระเงินของรายการนี้"));
        }

        var order = await _orderRepository.GetByIdAsync(payment.ORDER_ID, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure<PaymentOpsQueueDto>(DomainError.NotFound("ไม่พบคำสั่งซื้อของรายการนี้"));
        }

        var buyerEmail = await _userContactReader.GetEmailAsync(order.USER_ID, cancellationToken).ConfigureAwait(false);

        switch (request.Action)
        {
            case PaymentOpsResolutionAction.Refund:
                if (!string.IsNullOrWhiteSpace(payment.PROVIDER_PAYMENT_INTENT_ID))
                {
                    var refundResult = await _paymentMethod.CreateRefundAsync(
                        new CreateRefundRequest(payment.PROVIDER_PAYMENT_INTENT_ID, payment.AMOUNT, request.RefundReason),
                        cancellationToken).ConfigureAwait(false);

                    if (refundResult.IsFailure)
                    {
                        _logger.LogWarning("Stripe refund failed for Payment {PaymentId}: {Error}", payment.PAYMENT_ID, refundResult.Error.Message);
                    }
                }

                payment.MarkRefunded(_clock);

                if (order.PROMO_CODE_ID.HasValue)
                {
                    await _promoCodeRepository.RevertRedemptionAsync(order.PROMO_CODE_ID.Value, order.ORDER_ID, cancellationToken).ConfigureAwait(false);
                }

                if (_revenueSplitContract is not null)
                {
                    var itemIds = order.ORDER_ITEMS.Select(i => i.ORDER_ITEM_ID).ToList();
                    await _revenueSplitContract.ReverseRevenueSplitsForOrderAsync(order.ORDER_ID, itemIds, cancellationToken).ConfigureAwait(false);
                }

                if (buyerEmail is not null)
                {
                    _emailOutbox.Enqueue(
                        toEmail: buyerEmail,
                        subject: $"แจ้งผลการคืนเงินสำหรับคำสั่งซื้อ {order.ORDER_NO}",
                        bodyHtml: $"<p>เราได้ดำเนินการคืนเงินจำนวน ฿{payment.AMOUNT:N2} สำหรับคำสั่งซื้อ <strong>{order.ORDER_NO}</strong> เรียบร้อยแล้ว</p>",
                        templateKey: "order-refunded");
                }
                break;

            case PaymentOpsResolutionAction.ReopenAndFulfillOrder:
            case PaymentOpsResolutionAction.GrantAccessOnly:
                if (request.Action == PaymentOpsResolutionAction.ReopenAndFulfillOrder && order.STATUS != OrderStatus.Paid)
                {
                    order.MarkPaid(_clock);
                }

                var courseIds = order.ORDER_ITEMS
                    .Where(i => i.COURSE_ID.HasValue)
                    .Select(i => i.COURSE_ID!.Value)
                    .Distinct()
                    .ToList();

                var coursePrices = await _catalogPriceContract.GetPublishedCoursePricesAsync(courseIds, cancellationToken).ConfigureAwait(false);

                // One batched call (one query to load existing enrollments for this course set, one
                // SaveChangesAsync) instead of looping EnrollUserAsync per course — same fix already
                // applied to StripeWebhookHandler.HandlePaymentIntentSucceededAsync and
                // OrderService.CreateAsync's 100%-discount enroll path.
                var enrollmentGrants = courseIds
                    .Select(courseId =>
                    {
                        DateTime? expiresAtUtc = coursePrices.TryGetValue(courseId, out var enrolledCourseInfo) && enrolledCourseInfo.AccessDurationDays is { } days
                            ? _clock.UtcNow.AddDays(days)
                            : null;
                        return new CourseEnrollmentGrant(courseId, order.ORDER_ID, expiresAtUtc);
                    })
                    .ToList();

                await _learningAccessContract.EnrollUserInCoursesAsync(
                    order.USER_ID, "OpsResolution", enrollmentGrants, cancellationToken).ConfigureAwait(false);

                if (request.Action == PaymentOpsResolutionAction.ReopenAndFulfillOrder && _revenueSplitContract is not null)
                {
                    var splitItems = new List<OrderItemSplitInfo>();
                    foreach (var orderItem in order.ORDER_ITEMS)
                    {
                        if (orderItem.COURSE_ID.HasValue && coursePrices.TryGetValue(orderItem.COURSE_ID.Value, out var courseInfo))
                        {
                            splitItems.Add(new OrderItemSplitInfo(orderItem.ORDER_ITEM_ID, courseInfo.InstructorId, orderItem.LINE_TOTAL));
                        }
                    }

                    if (splitItems.Count > 0)
                    {
                        await _revenueSplitContract.RecordRevenueSplitsAsync(order.ORDER_ID, splitItems, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (buyerEmail is not null)
                {
                    _emailOutbox.Enqueue(
                        toEmail: buyerEmail,
                        subject: $"ยืนยันการเปิดสิทธิ์เข้าเรียนสำหรับคำสั่งซื้อ {order.ORDER_NO}",
                        bodyHtml: $"<p>เจ้าหน้าที่ได้ตรวจสอบและเปิดสิทธิ์การเข้าเรียนสำหรับคำสั่งซื้อ <strong>{order.ORDER_NO}</strong> ให้เรียบรแล้ว ขอให้มีความสุขกับการเรียนรู้ครับ</p>",
                        templateKey: "ops-access-granted");
                }
                break;

            case PaymentOpsResolutionAction.Dismiss:
                item.Dismiss(adminUserId, request.Note ?? "Dismissed by admin", _clock);
                await _opsQueueRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await _orderRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

            default:
                break;
        }

        try
        {
            item.Resolve(adminUserId, request.Note, _clock);
            await _opsQueueRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _orderRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<PaymentOpsQueueDto>(DomainError.Conflict(ex.Message));
        }
    }

    public async Task<Result<PaymentOpsQueueDto>> DismissAsync(
        Guid id,
        Guid adminUserId,
        DismissPaymentOpsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var item = await _opsQueueRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<PaymentOpsQueueDto>(DomainError.NotFound("ไม่พบรายการในคิวตรวจสอบการชำระเงินที่ระบุ"));
        }

        try
        {
            item.Dismiss(adminUserId, request.Note, _clock);
            await _opsQueueRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<PaymentOpsQueueDto>(DomainError.Conflict(ex.Message));
        }
    }
}

public sealed record PaymentOpsQueueDto(
    Guid Id,
    Guid PaymentId,
    string Reason,
    PaymentOpsQueueStatus Status,
    Guid? AssignedToUserId,
    Guid? ResolvedByUserId,
    DateTime? ResolvedAtUtc,
    string? Note,
    Guid? OrderId,
    string? OrderNo,
    Guid? UserId,
    string? UserEmail,
    decimal Amount,
    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus,
    OrderStatus OrderStatus,
    string? ProviderPaymentIntentId);

public enum PaymentOpsResolutionAction
{
    Refund,
    ReopenAndFulfillOrder,
    GrantAccessOnly,
    Dismiss
}

public sealed record ResolvePaymentOpsRequest(
    PaymentOpsResolutionAction Action,
    string? Note,
    string? RefundReason = null);

public sealed record DismissPaymentOpsRequest(string Note);
