namespace Siri.Modules.Payout.Contracts;

public sealed record OrderItemSplitInfo(Guid OrderItemId, Guid InstructorId, decimal GrossAmount);

public interface IRevenueSplitContract
{
    Task RecordRevenueSplitsAsync(
        Guid orderId,
        IReadOnlyList<OrderItemSplitInfo> items,
        CancellationToken cancellationToken);
}
