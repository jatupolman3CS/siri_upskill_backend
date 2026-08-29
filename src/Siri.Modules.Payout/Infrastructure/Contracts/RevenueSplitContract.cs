using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Contracts;
using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Infrastructure.Contracts;

public sealed class RevenueSplitContract(
    IRevenueSplitRepository repository,
    ICatalogPriceContract catalogPriceContract,
    IOptions<PayoutOptions> options,
    IClock clock,
    ILogger<RevenueSplitContract>? logger = null) : IRevenueSplitContract
{
    private const decimal DefaultRevenueSharePercent = 70.00m;

    public async Task RecordRevenueSplitsAsync(
        Guid orderId,
        IReadOnlyList<OrderItemSplitInfo> items,
        CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            return;
        }

        var periodKey = clock.UtcNow.ToString("yyyy-MM");
        var payoutOptions = options.Value;

        if (payoutOptions.EstimatedPaymentFeePercent == 0m)
        {
            logger?.LogWarning(
                "EstimatedPaymentFeePercent is set to 0.00% in PayoutOptions. Revenue splits will be calculated without payment fee deduction unless explicit PaymentFee is supplied.");
        }

        var instructorIds = items
            .Where(i => i.InstructorId != Guid.Empty)
            .Select(i => i.InstructorId)
            .Distinct()
            .ToList();

        var instructorSharePercents = await catalogPriceContract
            .GetInstructorRevenueSharePercentsAsync(instructorIds, cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in items)
        {
            if (item.OrderItemId == Guid.Empty || item.InstructorId == Guid.Empty)
            {
                continue;
            }

            var existing = await repository.GetByOrderItemIdAsync(item.OrderItemId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                continue; // Idempotent no-op
            }

            var sharePercent = instructorSharePercents.TryGetValue(item.InstructorId, out var customPercent)
                ? customPercent
                : DefaultRevenueSharePercent;

            var gross = item.GrossAmount;
            var paymentFee = item.PaymentFee ?? Math.Round(gross * payoutOptions.EstimatedPaymentFeePercent / 100m, 2, MidpointRounding.AwayFromZero);
            var netAmount = gross - paymentFee;
            if (netAmount < 0m)
            {
                netAmount = 0m;
            }

            var instructorAmount = Math.Round(netAmount * sharePercent / 100m, 2, MidpointRounding.AwayFromZero);
            var platformAmount = netAmount - instructorAmount; // Remainder to platform

            var split = REVENUE_SPLIT.Create(
                orderItemId: item.OrderItemId,
                instructorId: item.InstructorId,
                grossAmount: gross,
                paymentFeeAmount: paymentFee,
                platformFeeAmount: platformAmount,
                instructorAmount: instructorAmount,
                revenueSharePercent: sharePercent,
                periodKey: periodKey);

            repository.Add(split);
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReverseRevenueSplitsForOrderAsync(
        Guid orderId,
        IReadOnlyList<Guid> orderItemIds,
        CancellationToken cancellationToken)
    {
        if (orderItemIds is null || orderItemIds.Count == 0)
        {
            return;
        }

        var splits = await repository.GetByOrderItemIdsAsync(orderItemIds, cancellationToken).ConfigureAwait(false);
        if (splits.Count == 0)
        {
            return;
        }

        var currentPeriodKey = clock.UtcNow.ToString("yyyy-MM");

        foreach (var split in splits)
        {
            if (split.STATUS == RevenueSplitStatus.Pending || split.STATUS == RevenueSplitStatus.Payable)
            {
                split.Reverse();
            }
            else if (split.STATUS == RevenueSplitStatus.Paid)
            {
                // Refund after payout: record negative adjustment in current period, never modify past batches
                var adjustment = REVENUE_SPLIT.CreateAdjustment(
                    orderItemId: split.ORDER_ITEM_ID,
                    instructorId: split.INSTRUCTOR_ID,
                    grossAmount: -split.GROSS_AMOUNT,
                    paymentFeeAmount: -split.PAYMENT_FEE_AMOUNT,
                    platformFeeAmount: -split.PLATFORM_FEE_AMOUNT,
                    instructorAmount: -split.INSTRUCTOR_AMOUNT,
                    revenueSharePercent: split.REVENUE_SHARE_PERCENT,
                    periodKey: currentPeriodKey);

                repository.Add(adjustment);
            }
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
