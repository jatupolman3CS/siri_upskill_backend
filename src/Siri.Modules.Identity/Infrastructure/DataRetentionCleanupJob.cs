using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Nightly data retention cleanup job enforcing PDPA and storage policies (P7-04 / docs/SECURITY.md §4):
/// 1. Purges expired/consumed UserSecurityTokens older than 7 days.
/// 2. Purges unconfirmed User registrations older than 30 days.
/// 3. Purges revoked UserSessions older than 90 days.
/// </summary>
public sealed class DataRetentionCleanupJob(
    AppDbContext dbContext,
    IClock clock,
    ILogger<DataRetentionCleanupJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        logger.LogInformation("Starting PDPA DataRetentionCleanupJob at {Now}", now);

        // 1. Purge expired UserSecurityTokens older than 7 days
        var tokenCutoff = now.AddDays(-7);
        var deletedTokens = await dbContext.UserSecurityTokens()
            .Where(t => t.ExpiresAtUtc < tokenCutoff || (t.ConsumedAtUtc != null && t.ConsumedAtUtc < tokenCutoff))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // 2. Purge unconfirmed registrations older than 30 days
        var unconfirmedCutoff = now.AddDays(-30);
        var deletedPendingUsers = await dbContext.Users()
            .Where(u => u.Status == UserStatus.PendingEmailConfirmation && u.CreatedAtUtc < unconfirmedCutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // 3. Purge revoked sessions older than 90 days
        var sessionCutoff = now.AddDays(-90);
        var deletedSessions = await dbContext.UserSessions()
            .Where(s => s.RevokedAtUtc != null && s.RevokedAtUtc < sessionCutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "PDPA DataRetentionCleanupJob completed: {Tokens} tokens, {PendingUsers} pending users, {Sessions} sessions purged.",
            deletedTokens, deletedPendingUsers, deletedSessions);
    }
}
