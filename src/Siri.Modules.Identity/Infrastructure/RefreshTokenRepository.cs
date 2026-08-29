using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Application;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;

namespace Siri.Modules.Identity.Infrastructure;

public sealed class RefreshTokenRepository(AppDbContext dbContext) : IRefreshTokenRepository
{
    public Task<REFRESH_TOKEN?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(REFRESH_TOKEN token, CancellationToken cancellationToken)
    {
        dbContext.RefreshTokens().Add(token);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(REFRESH_TOKEN token, CancellationToken cancellationToken)
    {
        dbContext.RefreshTokens().Update(token);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
