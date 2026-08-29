using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.ListSessions;

/// <summary>
/// Lists the authenticated caller's own <see cref="Domain.USER_SESSION"/>s — every currently-active one,
/// plus any revoked within <see cref="RecentRevocationWindow"/>, so the caller can see "you were signed
/// out of this device on [date]" instead of a revoked device simply vanishing with no explanation (task
/// instruction: "consider whether recently-revoked ones ... are also useful ... make a specific,
/// justified choice"). <see cref="ListSessionsCommand.UserId"/> is always
/// <see cref="Siri.SharedKernel.IUserContext.UserId"/> — see that command's own doc comment for why
/// there is no other, client-suppliable source for it (this task's own IDOR-prevention point applied to
/// the read side: the query below filters on <c>UserId</c> directly, so there is no code path that can
/// ever return a session belonging to anyone else).
/// <para>
/// <b>Why 30 days for <see cref="RecentRevocationWindow"/></b>: reuses the exact span
/// <c>Login.LoginHandler</c>/<c>Refresh.RefreshHandler</c> already use for a refresh token's own
/// lifetime (each defines its own local <c>RefreshTokenLifetime</c> constant — this follows that same
/// established, if locally-duplicated, pattern rather than inventing an unrelated number). A revoked
/// session's refresh token would have expired on its own within that same span anyway, so showing
/// revocations older than that would surface devices whose access was already going to lapse for an
/// unrelated reason, diluting the "did something suspicious just happen recently" signal this window
/// exists to preserve.
/// </para>
/// <para>
/// Returns <see cref="ListSessionsResponse"/> directly, not wrapped in <see cref="Result{TValue}"/> —
/// unlike every other handler in this module, there is no expected-failure branch to encode here: any
/// authenticated caller can always list their own (possibly empty) session list, so a
/// <see cref="Result{TValue}"/>'s <c>IsFailure</c> branch would be permanently dead code. Every other
/// Identity handler's <see cref="Result{TValue}"/> wrapping exists specifically because it has a real
/// rejection path (invalid token, wrong credentials, ...) — this one deliberately does not pretend to.
/// </para>
/// </summary>
public sealed class ListSessionsHandler(AppDbContext dbContext, IClock clock)
{
    private static readonly TimeSpan RecentRevocationWindow = TimeSpan.FromDays(30);

    public async Task<ListSessionsResponse> HandleAsync(ListSessionsCommand command, CancellationToken cancellationToken)
    {
        var revokedSinceUtc = clock.UtcNow - RecentRevocationWindow;

        var sessions = await dbContext.UserSessions()
            .AsNoTracking()
            .Where(s => s.UserId == command.UserId && (s.RevokedAtUtc == null || s.RevokedAtUtc >= revokedSinceUtc))
            .OrderByDescending(s => s.LastSeenAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var summaries = sessions.Select(s => s.ToSummary(command.CurrentSessionId)).ToList();

        return new ListSessionsResponse(summaries);
    }
}
