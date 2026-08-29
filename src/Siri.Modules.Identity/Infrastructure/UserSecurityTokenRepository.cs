using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Application;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;

namespace Siri.Modules.Identity.Infrastructure;

public sealed class UserSecurityTokenRepository(AppDbContext dbContext) : IUserSecurityTokenRepository
{
    public Task<USER_SECURITY_TOKEN?> GetValidTokenAsync(Guid userId, UserSecurityTokenPurpose purpose, string tokenHash, CancellationToken cancellationToken) =>
        dbContext.UserSecurityTokens()
            .FirstOrDefaultAsync(
                t => t.UserId == userId &&
                     t.Purpose == purpose &&
                     t.TokenHash == tokenHash &&
                     t.ConsumedAtUtc == null &&
                     t.ExpiresAtUtc > DateTime.UtcNow,
                cancellationToken);

    public async Task AddAsync(USER_SECURITY_TOKEN token, CancellationToken cancellationToken)
    {
        dbContext.UserSecurityTokens().Add(token);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(USER_SECURITY_TOKEN token, CancellationToken cancellationToken)
    {
        dbContext.UserSecurityTokens().Update(token);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
