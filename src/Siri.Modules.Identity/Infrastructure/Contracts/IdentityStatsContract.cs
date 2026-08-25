using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;

namespace Siri.Modules.Identity.Infrastructure.Contracts;

public sealed class IdentityStatsContract(AppDbContext dbContext) : IIdentityStatsContract
{
    public async Task<LearnerStats> GetLearnerStatsAsync(DateTime todayUtc, CancellationToken cancellationToken)
    {
        var todayStart = todayUtc.Date;

        var totalLearners = await dbContext.Users()
            .AsNoTracking()
            .Where(u => u.Roles.Any(r => r.Name == "Learner") && u.Status == UserStatus.Active)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var todayNewLearners = await dbContext.Users()
            .AsNoTracking()
            .Where(u => u.Roles.Any(r => r.Name == "Learner") && u.CreatedAtUtc >= todayStart)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        return new LearnerStats(todayNewLearners, totalLearners);
    }
}
