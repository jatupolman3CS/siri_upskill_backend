using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.RevokeOtherSessions;

/// <summary>
/// Revokes every currently-active <see cref="UserSession"/> belonging to the authenticated caller
/// EXCEPT the one making this very request — the "sign out all my other devices, keep me logged in
/// here" pattern (task instruction's own example: "many apps offer 'sign out all other sessions'
/// specifically as a security action after noticing something suspicious").
/// <para>
/// <b>Design decision — this is P0-18's primary "revoke all" endpoint, and it excludes the current
/// session by default</b> (task instruction: "pick one as the primary behavior for this endpoint and
/// document why"). A device-management page (docs/SECURITY.md §2, this task's whole reason for
/// existing) is, by construction, a page the caller is looking at <em>right now</em>, from a session
/// that is itself one of the rows on that page. A "sign out everywhere" action that silently also signs
/// out the very tab the caller clicked the button from would be a surprising, self-defeating default —
/// the caller would immediately lose the page that told them the action succeeded, and would have to log
/// back in just to confirm it worked. Every mainstream consumer platform's "sign out other sessions"
/// affordance keeps the current one alive by default for exactly this reason, and it is also the safer
/// choice: nothing about "I want to kick out devices I don't recognize" requires also kicking out the
/// device I'm using to do that.
/// </para>
/// <para>
/// <c>RevokeAllSessions.RevokeAllSessionsHandler</c> — a second, separately-named endpoint (task
/// instruction explicitly allows building both "if trivially achievable", which this is once
/// <see cref="RevokeOtherSessionsCommand.CurrentSessionId"/> is available from the same "sid" claim work
/// item 1 already required) — exists as the deliberate, differently-named opt-in for "no, I mean
/// literally everywhere, including here", the same "I think my account is compromised" scenario
/// <c>ResetPasswordHandler</c> already handles unconditionally after a password reset.
/// </para>
/// <para>
/// Reuses the exact revoke-a-batch-of-sessions mechanics <c>ResetPasswordHandler.RevokeAllActiveSessionsAsync</c>
/// established (load every active session for the user, load every active refresh token for those
/// sessions in one query — not N, database.md: "ห้าม N+1" — revoke both, one atomic
/// <see cref="AppDbContext.SaveChangesAsync"/>, then post-commit, fail-open Redis mirror cleanup per
/// revoked session), the only difference being the extra <c>s.Id != CurrentSessionId</c> filter this
/// endpoint's whole purpose is built around. A distinct <see cref="SecurityAudit"/> row is written for
/// the whole action — one summary row listing every revoked session id, the same "one row per bulk
/// action, not one per session" granularity <c>ResetPasswordHandler</c>'s own bulk revoke already uses
/// (as opposed to SE-03 eviction's "one row per evicted session": that one is per-session because each
/// eviction is logically its own event competing for a spot in the concurrent-session limit; this one is
/// a single caller-initiated action that happens to touch several rows at once).
/// </para>
/// </summary>
public sealed class RevokeOtherSessionsHandler(
    AppDbContext dbContext,
    IClock clock,
    ISessionRegistry sessionRegistry,
    ILogger<RevokeOtherSessionsHandler> logger)
{
    /// <summary>Distinct from <c>RevokeSession.RevokeSessionHandler</c>'s single-session
    /// "revoked_by_user" — this is a bulk action, not a single explicit choice of one device, and
    /// keeping them distinguishable in <c>UserSessions.RevokeReason</c> matters for later investigation.</summary>
    private const string RevokeReasonValue = "revoked_by_user_bulk_others";

    private const string EventType = "session.revoked_by_user_bulk_others";

    private const string SuccessMessage = "ออกจากระบบในอุปกรณ์อื่นทั้งหมดเรียบร้อยแล้ว อุปกรณ์นี้ยังคงเข้าสู่ระบบอยู่";

    public async Task<RevokeOtherSessionsResponse> HandleAsync(
        RevokeOtherSessionsCommand command, string? ipAddress, CancellationToken cancellationToken)
    {
        var activeSessions = await dbContext.UserSessions()
            .Where(s => s.UserId == command.UserId && s.RevokedAtUtc == null && s.Id != command.CurrentSessionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activeSessions.Count == 0)
        {
            return new RevokeOtherSessionsResponse(SuccessMessage, 0);
        }

        var sessionIds = activeSessions.Select(s => s.Id).ToHashSet();

        // One query for every active session's still-active refresh token(s), not one query per
        // session (database.md: "ห้าม N+1") — same pattern ResetPasswordHandler's own bulk revoke uses.
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
            "RevokeOtherSessions: user {UserId} revoked {Count} other session(s), keeping session {CurrentSessionId} active.",
            command.UserId,
            sessionIds.Count,
            command.CurrentSessionId);

        return new RevokeOtherSessionsResponse(SuccessMessage, sessionIds.Count);
    }
}
