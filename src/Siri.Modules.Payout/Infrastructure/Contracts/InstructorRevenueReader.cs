using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Contracts;

namespace Siri.Modules.Payout.Infrastructure.Contracts;

/// <summary>
/// Implementation of <see cref="IInstructorRevenueReader"/> (docs/contracts/P11-10-instructor-dashboard-summary.md §2.2). Reads through the revenue-split
/// repository; the aggregate runs in the database (<c>IX_REVENUE_SPLITS_PAYOUT</c> = INSTRUCTOR_ID, PERIOD_KEY, STATUS).
/// </summary>
public sealed class InstructorRevenueReader(IRevenueSplitRepository repository) : IInstructorRevenueReader
{
    public Task<decimal> GetNetRevenueForPeriodAsync(Guid instructorProfileId, string periodKey, CancellationToken cancellationToken)
    {
        if (instructorProfileId == Guid.Empty || string.IsNullOrWhiteSpace(periodKey))
        {
            return Task.FromResult(0m);
        }

        return repository.GetNetInstructorAmountForPeriodAsync(instructorProfileId, periodKey.Trim(), cancellationToken);
    }
}
