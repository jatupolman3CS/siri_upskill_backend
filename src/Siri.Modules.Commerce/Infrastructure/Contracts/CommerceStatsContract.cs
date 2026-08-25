using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Contracts;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure.Contracts;

public sealed class CommerceStatsContract(AppDbContext dbContext) : ICommerceStatsContract
{
    public async Task<CommerceDashboardStats> GetCommerceDashboardStatsAsync(DateTime todayUtc, CancellationToken cancellationToken)
    {
        var todayStart = todayUtc.Date;

        var todaySales = await dbContext.Payments()
            .AsNoTracking()
            .Where(p => p.STATUS == PaymentStatus.Succeeded && p.SUCCEEDED_AT_UTC >= todayStart)
            .SumAsync(p => (decimal?)p.AMOUNT, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        var pendingRefundsCount = await dbContext.Refunds()
            .AsNoTracking()
            .Where(r => r.STATUS == RefundStatus.Requested)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CommerceDashboardStats(todaySales, pendingRefundsCount);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetDailyCourseRevenueAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var startUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var rows = await (
            from item in dbContext.OrderItems().AsNoTracking()
            join order in dbContext.Orders().AsNoTracking() on item.ORDER_ID equals order.ORDER_ID
            where order.STATUS == OrderStatus.Paid
               && (order.PAID_AT_UTC ?? order.CreatedAtUtc) >= startUtc
               && (order.PAID_AT_UTC ?? order.CreatedAtUtc) <= endUtc
               && item.COURSE_ID != null
            group item by item.COURSE_ID!.Value into g
            select new { CourseId = g.Key, Revenue = g.Sum(x => x.LINE_TOTAL) }
        ).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.ToDictionary(x => x.CourseId, x => x.Revenue);
    }
}
