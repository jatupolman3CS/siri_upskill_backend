using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Payment;
using Siri.Integrations.Payment.Stripe;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Payout.Contracts;
using Siri.SharedKernel;
using Stripe;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// Handles incoming signature-verified Stripe webhooks.
/// Guarantees idempotency via <see cref="IStripeWebhookEventRepository"/> and updates order/payment state machines.
/// </summary>
public sealed class StripeWebhookHandler
{
    private readonly IStripeWebhookEventRepository _webhookEventRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IPromoCodeRepository _promoCodeRepository;
    private readonly IPaymentOpsQueueRepository _paymentOpsQueueRepository;
    private readonly ICatalogPriceContract _catalogPriceContract;
    private readonly ILiveScheduleReader _liveScheduleReader;
    private readonly ILearningAccessContract _learningAccessContract;
    private readonly IRevenueSplitContract _revenueSplitContract;
    private readonly IEmailOutbox _emailOutbox;
    private readonly IUserContactReader _userContactReader;
    private readonly IPaymentMethod _paymentMethod;
    private readonly StripeOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<StripeWebhookHandler> _logger;

    public StripeWebhookHandler(
        IStripeWebhookEventRepository webhookEventRepository,
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        IPromoCodeRepository promoCodeRepository,
        IPaymentOpsQueueRepository paymentOpsQueueRepository,
        ICatalogPriceContract catalogPriceContract,
        ILiveScheduleReader liveScheduleReader,
        ILearningAccessContract learningAccessContract,
        IRevenueSplitContract revenueSplitContract,
        IEmailOutbox emailOutbox,
        IUserContactReader userContactReader,
        IPaymentMethod paymentMethod,
        IOptions<StripeOptions> options,
        IClock clock,
        ILogger<StripeWebhookHandler> logger)
    {
        _webhookEventRepository = webhookEventRepository;
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _promoCodeRepository = promoCodeRepository;
        _paymentOpsQueueRepository = paymentOpsQueueRepository;
        _catalogPriceContract = catalogPriceContract;
        _liveScheduleReader = liveScheduleReader;
        _learningAccessContract = learningAccessContract;
        _revenueSplitContract = revenueSplitContract;
        _emailOutbox = emailOutbox;
        _userContactReader = userContactReader;
        _paymentMethod = paymentMethod;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<string>> HandleAsync(
        string json,
        string? signatureHeader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            _logger.LogWarning("Stripe webhook received without Stripe-Signature header.");
            return Result.Failure<string>(DomainError.Validation("Missing Stripe-Signature header."));
        }

        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            _logger.LogError("Stripe:WebhookSecret is not configured.");
            return Result.Failure<string>(DomainError.Validation("Webhook secret is not configured."));
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                signatureHeader,
                _options.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature validation failed.");
            return Result.Failure<string>(DomainError.Validation("Invalid Stripe-Signature."));
        }

        // All repositories and module contracts share one DbContext. Commit fulfillment and the
        // webhook receipt together so a downstream failure remains safe for Stripe to retry.
        return await _orderRepository.ExecuteInTransactionAsync(async () =>
        {
            var existingEvent = await _webhookEventRepository.GetByStripeEventIdAsync(stripeEvent.Id, cancellationToken).ConfigureAwait(false);
            if (existingEvent != null)
            {
                _logger.LogInformation("Stripe webhook event {EventId} already processed (idempotent no-op).", stripeEvent.Id);
                return Result.Success("Event already processed");
            }

            var webhookEvent = STRIPE_WEBHOOK_EVENT.Create(stripeEvent.Id, stripeEvent.Type, json, _clock);

            switch (stripeEvent.Type)
            {
                case "payment_intent.succeeded":
                    await HandlePaymentIntentSucceededAsync(stripeEvent, webhookEvent, cancellationToken).ConfigureAwait(false);
                    break;

                case "payment_intent.payment_failed":
                    await HandlePaymentIntentFailedAsync(stripeEvent, webhookEvent, cancellationToken).ConfigureAwait(false);
                    break;

                case "payment_intent.canceled":
                    await HandlePaymentIntentCanceledAsync(stripeEvent, webhookEvent, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    webhookEvent.MarkProcessed($"unhandled_event:{stripeEvent.Type}", _clock);
                    break;
            }

            await _webhookEventRepository.AddAsync(webhookEvent, cancellationToken).ConfigureAwait(false);

            return Result.Success("Event processed");
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandlePaymentIntentSucceededAsync(
        Event stripeEvent,
        STRIPE_WEBHOOK_EVENT webhookEvent,
        CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not PaymentIntent paymentIntent)
        {
            webhookEvent.MarkProcessed("invalid_payment_intent_payload", _clock);
            return;
        }

        var payment = await _paymentRepository.GetByProviderPaymentIntentIdAsync(paymentIntent.Id, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            _logger.LogWarning("Payment for PaymentIntent {PaymentIntentId} not found in DB.", paymentIntent.Id);
            webhookEvent.MarkProcessed("payment.not_found", _clock);
            return;
        }

        var order = await _orderRepository.GetByIdAsync(payment.ORDER_ID, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} for Payment {PaymentId} not found in DB.", payment.ORDER_ID, payment.PAYMENT_ID);
            webhookEvent.MarkProcessed("order.not_found", _clock);
            return;
        }

        // Check if order was already cancelled/failed -> Do not swallow money silently! Flag into PAYMENT_OPS_QUEUE
        if (order.STATUS is OrderStatus.Cancelled or OrderStatus.Failed)
        {
            _logger.LogWarning("Order {OrderNo} was {Status} but payment succeeded. Adding to payment ops queue.", order.ORDER_NO, order.STATUS);
            if (payment.STATUS != PaymentStatus.Succeeded)
            {
                payment.MarkSucceeded(_clock);
            }

            var opsQueue = PAYMENT_OPS_QUEUE.Create(payment.PAYMENT_ID, "Payment succeeded after order was cancelled/expired");
            await _paymentOpsQueueRepository.AddAsync(opsQueue, cancellationToken).ConfigureAwait(false);
            webhookEvent.MarkProcessed("payment.succeeded_after_order_cancelled", _clock);
            return;
        }

        // The order was already paid by a different PaymentIntent (reload / PromptPay<->Card switch left two
        // chargeable intents) -> this charge is a duplicate. Flag it for ops/refund instead of silently
        // marking it Succeeded.
        if (order.STATUS == OrderStatus.Paid && payment.STATUS != PaymentStatus.Succeeded)
        {
            _logger.LogWarning("Order {OrderNo} is already Paid but payment {PaymentId} also succeeded. Adding to payment ops queue.", order.ORDER_NO, payment.PAYMENT_ID);
            if (payment.STATUS is PaymentStatus.Pending or PaymentStatus.Processing or PaymentStatus.Failed or PaymentStatus.Expired)
            {
                payment.MarkSucceeded(_clock);
            }

            var duplicateOpsQueue = PAYMENT_OPS_QUEUE.Create(payment.PAYMENT_ID, "Duplicate payment succeeded for an already paid order");
            await _paymentOpsQueueRepository.AddAsync(duplicateOpsQueue, cancellationToken).ConfigureAwait(false);
            webhookEvent.MarkProcessed("payment.succeeded_after_order_paid", _clock);
            return;
        }

        // Failed is included: a card decline marks the payment Failed, but the learner can retry another
        // card on the same PaymentIntent and succeed.
        if (payment.STATUS is PaymentStatus.Pending or PaymentStatus.Processing or PaymentStatus.Failed)
        {
            payment.MarkSucceeded(_clock);
        }

        if (order.STATUS == OrderStatus.AwaitingPayment)
        {
            order.MarkPaid(_clock);

            var courseIds = order.ORDER_ITEMS
                .Where(i => i.COURSE_ID.HasValue)
                .Select(i => i.COURSE_ID!.Value)
                .Distinct()
                .ToList();

            // Fetched once, up front: both the enrollment grant (needs AccessDurationDays, to avoid
            // silently handing out lifetime access on a time-limited course) and the revenue split
            // (needs InstructorId) read the same published-course snapshot.
            var coursePrices = await _catalogPriceContract.GetPublishedCoursePricesAsync(courseIds, cancellationToken).ConfigureAwait(false);

            // 1. Auto-enroll student into all courses in order — one batched call (one query to load
            // existing enrollments for this course set, one SaveChangesAsync) instead of looping
            // EnrollUserAsync per course. This is the payment-confirmation webhook: money is already
            // captured by Stripe, so granting access reliably in one round trip matters more here than
            // almost anywhere else in the codebase.
            // P11-13 (Q13.3): Live/Hybrid courses count AccessDurationDays from the first scheduled live
            // session's StartsAtUtc, not the purchase date — async per course, so this can no longer be a
            // plain LINQ .Select(). GetEarliestScheduledSessionAsync returning null covers both "course is
            // OnDemand" and "Live/Hybrid but no session scheduled yet" — both fall back to counting from
            // now, exactly the pre-P11-13 behavior (see docs/contracts/P11-13-access-duration-first-session.md §0.2).
            var enrollmentGrants = new List<CourseEnrollmentGrant>(courseIds.Count);
            foreach (var courseId in courseIds)
            {
                DateTime? expiresAtUtc = null;
                if (coursePrices.TryGetValue(courseId, out var enrolledCourseInfo) && enrolledCourseInfo.AccessDurationDays is { } days)
                {
                    var earliestSession = await _liveScheduleReader.GetEarliestScheduledSessionAsync(courseId, cancellationToken).ConfigureAwait(false);
                    // Never count from a session that has already started: late buyers would lose the
                    // elapsed time (or get an already-expired enrollment).
                    var accessStartUtc = earliestSession is not null && earliestSession.StartsAtUtc > _clock.UtcNow
                        ? earliestSession.StartsAtUtc
                        : _clock.UtcNow;
                    expiresAtUtc = accessStartUtc.AddDays(days);
                }
                enrollmentGrants.Add(new CourseEnrollmentGrant(courseId, order.ORDER_ID, expiresAtUtc));
            }

            var enrollmentResult = await _learningAccessContract.EnrollUserInCoursesAsync(
                order.USER_ID, "Purchase", enrollmentGrants, cancellationToken).ConfigureAwait(false);
            if (enrollmentResult.IsFailure)
            {
                throw new InvalidOperationException($"Payment fulfillment failed: {enrollmentResult.Error.Code}");
            }

            // 2. Record Revenue Splits for instructors. charge.balance_transaction.fee is a single value
            // for the whole PaymentIntent, but OrderItemSplitInfo is per line item — an order can buy
            // several courses at once, so the fee must be pro-rated by each item's share of the order
            // total rather than charged in full to every item (which would multiply the deducted fee
            // whenever there's more than one item). This applies to PromptPay too, not just Card — it's
            // just never shown up before because no caller ever passed a non-null PaymentFee.
            var feeResult = await _paymentMethod.GetChargeFeeAsync(paymentIntent.Id, cancellationToken).ConfigureAwait(false);
            var totalFee = feeResult.Value; // GetChargeFeeAsync always returns Success — .Value is safe here

            var splitItems = new List<OrderItemSplitInfo>();
            var qualifyingItems = order.ORDER_ITEMS
                .Where(i => i.COURSE_ID.HasValue && coursePrices.ContainsKey(i.COURSE_ID.Value))
                .ToList();

            if (totalFee is null || order.TOTAL_AMOUNT <= 0m)
            {
                // No real fee available (not yet settled by Stripe / API call failed) — let
                // RevenueSplitContract fall back to EstimatedPaymentFeePercent itself (Q4).
                foreach (var item in qualifyingItems)
                {
                    splitItems.Add(new OrderItemSplitInfo(item.ORDER_ITEM_ID, coursePrices[item.COURSE_ID!.Value].InstructorId, item.LINE_TOTAL));
                }
            }
            else
            {
                // Items excluded from the split (no course / course no longer published) keep their own
                // share of the fee out of the instructors' pool, so it isn't dumped on the last item.
                var excludedTotal = order.ORDER_ITEMS.Where(i => !qualifyingItems.Contains(i)).Sum(i => i.LINE_TOTAL);
                var qualifyingFee = totalFee.Value - Math.Round(totalFee.Value * excludedTotal / order.TOTAL_AMOUNT, 2, MidpointRounding.AwayFromZero);

                // Pro-rate the qualifying fee by each item's LINE_TOTAL share of TOTAL_AMOUNT — remainder goes
                // to the last item (same pattern as platformAmount in RevenueSplitContract) so the sum of
                // per-item fees always equals qualifyingFee exactly, never just "close" due to rounding.
                decimal allocatedSoFar = 0m;
                for (var i = 0; i < qualifyingItems.Count; i++)
                {
                    var item = qualifyingItems[i];
                    var instructorId = coursePrices[item.COURSE_ID!.Value].InstructorId;
                    decimal itemFee;
                    if (i == qualifyingItems.Count - 1)
                    {
                        itemFee = qualifyingFee - allocatedSoFar;
                    }
                    else
                    {
                        itemFee = Math.Round(totalFee.Value * item.LINE_TOTAL / order.TOTAL_AMOUNT, 2, MidpointRounding.AwayFromZero);
                        allocatedSoFar += itemFee;
                    }
                    splitItems.Add(new OrderItemSplitInfo(item.ORDER_ITEM_ID, instructorId, item.LINE_TOTAL, itemFee));
                }
            }

            if (splitItems.Count > 0)
            {
                await _revenueSplitContract.RecordRevenueSplitsAsync(order.ORDER_ID, splitItems, cancellationToken).ConfigureAwait(false);
            }

            // 3. Queue receipt email via outbox — identity.Users.Email is the real source of truth.
            // paymentIntent.ReceiptEmail is never populated anywhere upstream in this codebase (nothing
            // sets CreatePaymentIntentRequest.CustomerEmail), so relying on it alone silently sent every
            // receipt to a fabricated, non-deliverable address.
            var buyerEmail = await _userContactReader.GetEmailAsync(order.USER_ID, cancellationToken).ConfigureAwait(false);
            if (buyerEmail is not null)
            {
                _emailOutbox.Enqueue(
                    toEmail: buyerEmail,
                    subject: $"ใบเสร็จรับเงินสำหรับคำสั่งซื้อ {order.ORDER_NO}",
                    bodyHtml: $"<p>ขอบคุณที่สั่งซื้อคอร์สเรียนกับ SIRI UpSkill คำสั่งซื้อเลขที่ <strong>{order.ORDER_NO}</strong> ยอดชำระ ฿{order.TOTAL_AMOUNT:N2}</p>",
                    templateKey: "order-receipt");
            }
            else
            {
                _logger.LogWarning("Order {OrderNo} paid but no email found for user {UserId}; receipt not sent.", order.ORDER_NO, order.USER_ID);
            }
        }

        webhookEvent.MarkProcessed("payment.marked_succeeded", _clock);
    }

    private async Task HandlePaymentIntentFailedAsync(
        Event stripeEvent,
        STRIPE_WEBHOOK_EVENT webhookEvent,
        CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not PaymentIntent paymentIntent)
        {
            webhookEvent.MarkProcessed("invalid_payment_intent_payload", _clock);
            return;
        }

        var payment = await _paymentRepository.GetByProviderPaymentIntentIdAsync(paymentIntent.Id, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            webhookEvent.MarkProcessed("payment.not_found", _clock);
            return;
        }

        var failureMessage = paymentIntent.LastPaymentError?.Message ?? "Payment failed";
        if (payment.STATUS is PaymentStatus.Pending or PaymentStatus.Processing)
        {
            payment.MarkFailed(failureMessage);
        }

        webhookEvent.MarkProcessed("payment.marked_failed", _clock);
    }

    private async Task HandlePaymentIntentCanceledAsync(
        Event stripeEvent,
        STRIPE_WEBHOOK_EVENT webhookEvent,
        CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not PaymentIntent paymentIntent)
        {
            webhookEvent.MarkProcessed("invalid_payment_intent_payload", _clock);
            return;
        }

        var payment = await _paymentRepository.GetByProviderPaymentIntentIdAsync(paymentIntent.Id, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            webhookEvent.MarkProcessed("payment.not_found", _clock);
            return;
        }

        if (payment.STATUS is PaymentStatus.Pending or PaymentStatus.Processing)
        {
            payment.MarkExpired();

            var order = await _orderRepository.GetByIdAsync(payment.ORDER_ID, cancellationToken).ConfigureAwait(false);
            if (order is not null)
            {
                if (order.STATUS == OrderStatus.AwaitingPayment)
                {
                    order.MarkCancelled();
                }

                if (order.PROMO_CODE_ID.HasValue)
                {
                    await _promoCodeRepository.RevertRedemptionAsync(order.PROMO_CODE_ID.Value, order.ORDER_ID, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        webhookEvent.MarkProcessed("payment.marked_canceled", _clock);
    }
}
