namespace Siri.Modules.Identity.Contracts;

public sealed record LearnerStats(int TodayNewLearners, int TotalLearners);

public interface IIdentityStatsContract
{
    Task<LearnerStats> GetLearnerStatsAsync(DateTime todayUtc, CancellationToken cancellationToken);
}
