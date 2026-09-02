using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Logout;

public sealed class LogoutHandler(
    AppDbContext dbContext,
    ISecurityTokenGenerator tokenGenerator,
    ISessionRegistry sessionRegistry,
    IClock clock)
{
    private const string LogoutRevokeReason = "user_logged_out";

    public async Task HandleAsync(string? rawRefreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return;
        }

        var tokenHash = tokenGenerator.Hash(rawRefreshToken);
        var token = await dbContext.RefreshTokens()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (token is null || token.RevokedAtUtc is not null)
        {
            return;
        }

        token.Revoke(null, clock);

        var session = await dbContext.UserSessions()
            .FirstOrDefaultAsync(s => s.Id == token.SessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is not null && session.RevokedAtUtc is null)
        {
            session.Revoke(LogoutRevokeReason, clock);
            await sessionRegistry.RemoveAsync(session.UserId, session.Id, cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
