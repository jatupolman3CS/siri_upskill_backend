using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Application;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;

namespace Siri.Modules.Identity.Infrastructure;

public sealed class UserSessionRepository(AppDbContext dbContext) : IUserSessionRepository
{
    public Task<USER_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.UserSessions().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<USER_SESSION>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.UserSessions()
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(USER_SESSION session, CancellationToken cancellationToken)
    {
        dbContext.UserSessions().Add(session);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(USER_SESSION session, CancellationToken cancellationToken)
    {
        dbContext.UserSessions().Update(session);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
