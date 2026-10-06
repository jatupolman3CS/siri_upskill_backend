using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Payment;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>
/// Hangfire recurring job that automatically cancels stale orders in AwaitingPayment status
/// once their timeout (e.g. 30 minutes) expires, cancels associated Stripe PaymentIntents,
/// reverts promo code redemptions, and records audit trails.
/// </summary>
public sealed class OrderExpiryJob
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentMethod _paymentMethod;
    private readonly IPromoCodeRepository _promoCodeRepository;
    private readonly ICatalogPriceContract _catalogPriceContract;
    private readonly IClock _clock;
    private readonly OrderExpiryOptions _options;
    private readonly ILogger<OrderExpiryJob> _logger;

    public OrderExpiryJob(
        IOrderRepository orderRepository,
        IPaymentRepository paymentRepository,
        IPaymentMethod paymentMethod,
        IPromoCodeRepository promoCodeRepository,
        ICatalogPriceContract catalogPriceContract,
        IClock clock,
        IOptions<OrderExpiryOptions> options,
        ILogger<OrderExpiryJob> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethod = paymentMethod;
        _promoCodeRepository = promoCodeRepository;
        _catalogPriceContract = catalogPriceContract;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = _clock.UtcNow.AddMinutes(-_options.ExpiryMinutes);
        _logger.LogInformation("Running OrderExpiryJob at {NowUtc}, checking Pending/AwaitingPayment orders created on or before {CutoffUtc}", _clock.UtcNow, cutoff);

        var expiredOrders = await _orderRepository.GetStaleAwaitingPaymentOrdersAsync(cutoff, _options.BatchSize, cancellationToken).ConfigureAwait(false);

        if (expiredOrders.Count == 0)
        {
            _logger.LogDebug("OrderExpiryJob found no stale orders to expire.");
            return;
        }

        _logger.LogInformation("Found {Count} stale orders to expire.", expiredOrders.Count);

        var processedCount = 0;
        foreach (var order in expiredOrders)
        {
            try
            {
                await ExpireOrderAsync(order, cancellationToken).ConfigureAwait(false);
                processedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to expire order {OrderId} ({OrderNo})", order.ORDER_ID, order.ORDER_NO);
            }
        }

        _logger.LogInformation("OrderExpiryJob completed: {Processed}/{Total} orders successfully expired.", processedCount, expiredOrders.Count);
    }

    public async Task ExpireOrderAsync(ORDER order, CancellationToken cancellationToken)
    {
        await _orderRepository.ExecuteInTransactionAsync(async () =>
        {
            // Re-fetch inside transaction
            var currentOrder = await _orderRepository.GetByIdAsync(order.ORDER_ID, cancellationToken).ConfigureAwait(false);
            if (currentOrder is null || currentOrder.STATUS is not (OrderStatus.AwaitingPayment or OrderStatus.Pending))
            {
                _logger.LogInformation("Order {OrderNo} is no longer in Pending/AwaitingPayment status ({Status}); skipping expiry.", order.ORDER_NO, currentOrder?.STATUS);
                return;
            }

            var pendingPayments = await _paymentRepository.GetPendingByOrderIdAsync(currentOrder.ORDER_ID, cancellationToken).ConfigureAwait(false);
            foreach (var payment in pendingPayments)
            {
                if (!string.IsNullOrWhiteSpace(payment.PROVIDER_PAYMENT_INTENT_ID))
                {
                    var cancelResult = await _paymentMethod.CancelPaymentIntentAsync(payment.PROVIDER_PAYMENT_INTENT_ID, cancellationToken).ConfigureAwait(false);
                    if (cancelResult.IsFailure)
                    {
                        _logger.LogWarning("Failed to cancel PaymentIntent {IntentId} for Payment {PaymentId}: {Error}",
                            payment.PROVIDER_PAYMENT_INTENT_ID, payment.PAYMENT_ID, cancelResult.Error.Message);
                    }
                }

                payment.MarkExpired();
            }

            currentOrder.MarkCancelled();

            if (currentOrder.PROMO_CODE_ID.HasValue)
            {
                await _promoCodeRepository.RevertRedemptionAsync(currentOrder.PROMO_CODE_ID.Value, currentOrder.ORDER_ID, cancellationToken).ConfigureAwait(false);
            }

            // P11-11 §4.4: same seat-release reasoning as OrderService.CancelAsync — an order that
            // reserved a seat but expired before payment must give it back, atomically with the rest of
            // this already-wrapped transaction.
            foreach (var courseId in currentOrder.ORDER_ITEMS.Select(i => i.COURSE_ID).Where(id => id.HasValue).Select(id => id!.Value).Distinct())
            {
                await _catalogPriceContract.ReleaseSeatAsync(courseId, cancellationToken).ConfigureAwait(false);
            }

            await _orderRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Successfully expired and cancelled order {OrderNo} (ID: {OrderId}).", currentOrder.ORDER_NO, currentOrder.ORDER_ID);
        }, cancellationToken).ConfigureAwait(false);
    }
}
