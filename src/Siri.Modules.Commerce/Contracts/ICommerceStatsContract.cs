namespace Siri.Modules.Commerce.Contracts;

public sealed record CommerceDashboardStats(decimal TodaySales, int PendingRefundsCount);

public interface ICommerceStatsContract
{
    Task<CommerceDashboardStats> GetCommerceDashboardStatsAsync(DateTime todayUtc, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, decimal>> GetDailyCourseRevenueAsync(DateOnly date, CancellationToken cancellationToken);
}

