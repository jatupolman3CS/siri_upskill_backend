using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Contracts;
using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Infrastructure.Contracts;

public sealed class RevenueSplitContract(
    IRevenueSplitRepository repository,
    IClock clock) : IRevenueSplitContract
{
    private const decimal InstructorShareRatio = 0.70m;
    private const decimal PlatformShareRatio = 0.30m;

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

            var gross = item.GrossAmount;
            var paymentFee = 0m; // Stripe fee calculation if applicable
            var instructorAmount = Math.Round(gross * InstructorShareRatio, 2);
            var platformAmount = gross - instructorAmount;

            var split = REVENUE_SPLIT.Create(
                orderItemId: item.OrderItemId,
                instructorId: item.InstructorId,
                grossAmount: gross,
                paymentFeeAmount: paymentFee,
                platformFeeAmount: platformAmount,
                instructorAmount: instructorAmount,
                periodKey: periodKey);

            repository.Add(split);
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
