using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.RevokeAllSessions;

/// <summary>
/// Revokes EVERY currently-active <see cref="UserSession"/> belonging to the authenticated caller,
/// including the one making this very request — the literal "sign out everywhere" action, as opposed to
/// <c>RevokeOtherSessions.RevokeOtherSessionsHandler</c>'s "everywhere except here".
/// <para>
/// <b>Design decision — this is the secondary, explicitly-named "revoke absolutely everything"
/// endpoint, not P0-18's default</b> (task instruction: "pick one as the primary behavior ... document
/// why"; see <c>RevokeOtherSessionsHandler</c>'s doc comment for the primary-endpoint reasoning this
/// one deliberately does not repeat). This exists because "everywhere except here" is not always what a
/// caller wants — someone who suspects their account credentials themselves (not just one stray device)
/// were compromised has a real reason to want every session gone, including the one they are currently
/// using, so they can go re-authenticate from a position of "I know every prior session is dead". This
/// mirrors <c>ResetPasswordHandler</c>'s own unconditional "revoke every active session" behavior after a
/// password reset — same underlying scenario (the caller no longer trusts their prior sessions), just
/// triggered here by an explicit request from an already-authenticated caller instead of by a password
/// reset's side effect (exactly the distinction the task instructions draw between the two).
/// </para>
/// <para>
/// Reuses the same batch-revoke mechanics <c>RevokeOtherSessionsHandler</c>/<c>ResetPasswordHandler</c>
/// already established (load every active session, load every active refresh token for those sessions
/// in one query, revoke both, one atomic <see cref="AppDbContext.SaveChangesAsync"/>, then post-commit,
/// fail-open Redis mirror cleanup per revoked session) — simply without the "except current" filter.
/// </para>
/// </summary>
public sealed class RevokeAllSessionsHandler(
    AppDbContext dbContext,
    IClock clock,
    ISessionRegistry sessionRegistry,
    ILogger<RevokeAllSessionsHandler> logger)
{
    /// <summary>Distinct from <c>RevokeSession</c>'s single-session reason and
    /// <c>RevokeOtherSessions</c>'s "...bulk_others" — this one genuinely includes the current session,
    /// which the other two never do (one by definition, the other by design), so
    /// <c>UserSessions.RevokeReason</c> stays self-explanatory about which of the three paths a given
    /// row went through.</summary>
    private const string RevokeReasonValue = "revoked_by_user_bulk_all";

    private const string EventType = "session.revoked_by_user_bulk_all";

    private const string SuccessMessage = "ออกจากระบบในทุกอุปกรณ์เรียบร้อยแล้ว รวมถึงอุปกรณ์นี้ด้วย";

    public async Task<RevokeAllSessionsResponse> HandleAsync(
        RevokeAllSessionsCommand command, string? ipAddress, CancellationToken cancellationToken)
    {
        var activeSessions = await dbContext.UserSessions()
            .Where(s => s.UserId == command.UserId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activeSessions.Count == 0)
        {
            return new RevokeAllSessionsResponse(SuccessMessage, 0);
        }

        var sessionIds = activeSessions.Select(s => s.Id).ToHashSet();

        // One query for every active session's still-active refresh token(s), not one query per
        // session (database.md: "ห้าม N+1").
        var activeTokens = await dbContext.RefreshTokens()
            .Where(t => sessionIds.Contains(t.SessionId) && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var session in activeSessions)
        {
            session.Revoke(RevokeReasonValue, clock);
        }

        foreach (var token in activeTokens)
        {
            token.Revoke(null, clock);
        }

        var detail = $$"""{"revokedSessionIds":[{{string.Join(",", sessionIds.Select(id => $"\"{id}\""))}}]}""";
        dbContext.SecurityAudits().Add(SecurityAudit.Record(EventType, command.UserId, detail, ipAddress, clock));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Redis mirror: only after the DB-authoritative save above succeeds — same ordering/fail-open
        // reasoning as every other revocation path in this module.
        foreach (var sessionId in sessionIds)
        {
            await sessionRegistry.RemoveAsync(command.UserId, sessionId, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "RevokeAllSessions: user {UserId} revoked all {Count} active session(s), including their own current one.",
            command.UserId,
            sessionIds.Count);

        return new RevokeAllSessionsResponse(SuccessMessage, sessionIds.Count);
    }
}
