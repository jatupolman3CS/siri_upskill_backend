namespace Siri.Modules.Payout.Contracts;

public sealed record OrderItemSplitInfo(
    Guid OrderItemId,
    Guid InstructorId,
    decimal GrossAmount,
    decimal? PaymentFee = null);

public interface IRevenueSplitContract
{
    Task RecordRevenueSplitsAsync(
        Guid orderId,
        IReadOnlyList<OrderItemSplitInfo> items,
        CancellationToken cancellationToken);

    Task ReverseRevenueSplitsForOrderAsync(
        Guid orderId,
        IReadOnlyList<Guid> orderItemIds,
        CancellationToken cancellationToken);
}
