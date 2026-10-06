using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Notification.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Turns an already-authenticated <see cref="USER"/> into a signed-in session: records the login,
/// starts a <see cref="USER_SESSION"/> with its <see cref="REFRESH_TOKEN"/>, mints the access token,
/// enforces the SE-03 concurrent-session limit and mirrors the session into Redis. Shared by every way
/// of proving who you are (email + password in <c>LoginHandler</c>, Google in <c>GoogleLoginHandler</c>)
/// so a new sign-in method can never skip the session rules.
/// <para>
/// Everything is staged on the caller's <see cref="AppDbContext"/> and committed in one
/// <see cref="AppDbContext.SaveChangesAsync"/> call (database.md: atomic multi-table writes) — which
/// also commits whatever the caller staged before calling <see cref="IssueAsync"/> (for instance a
/// brand-new user). Redis is touched only after that save succeeds; see <see cref="ISessionRegistry"/>
/// for why a Redis failure must never undo a login that already committed.
/// </para>
/// </summary>
public sealed class LoginSessionIssuer(
    AppDbContext dbContext,
    ISecurityTokenGenerator refreshTokenGenerator,
    IAccessTokenGenerator accessTokenGenerator,
    IClock clock,
    IOptions<ConcurrentSessionOptions> concurrentSessionOptions,
    ISessionRegistry sessionRegistry,
    IEmailOutbox emailOutbox,
    ILogger<LoginSessionIssuer> logger)
{
    /// <summary>30 days per security.md ("refresh token 30 วัน แบบ rotation + reuse detection") — also
    /// the Redis mirror's TTL (ARCHITECTURE.md §5: "session:{userId}:{sessionId} (TTL = refresh
    /// token)"), reused as the exact same constant rather than a second copy that could drift.</summary>
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    /// <summary><see cref="USER_SESSION.RevokeReason"/> for an SE-03 eviction — distinct from
    /// Refresh's "suspected_refresh_token_reuse" so <c>UserSessions</c> rows are self-explanatory
    /// without joining out to <see cref="SECURITY_AUDIT"/>.</summary>
    private const string ConcurrentSessionLimitRevokeReason = "concurrent_session_limit_exceeded";

    /// <summary><see cref="SECURITY_AUDIT.EventType"/> for an SE-03 eviction — distinct from
    /// plain "login.succeeded" and Refresh's "refresh_token.reuse_detected", so this specific event is
    /// filterable in <c>SecurityAudits</c>.</summary>
    private const string ConcurrentSessionLimitEventType = "session.evicted_concurrent_limit";

    public async Task<LoginResult> IssueAsync(
        USER user,
        string? deviceId,
        string? deviceName,
        string? userAgent,
        string? ipAddress,
        string auditEventType,
        string? auditDetail,
        CancellationToken cancellationToken)
    {
        user.RecordLogin(clock);

        var resolvedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? Guid.NewGuid().ToString() : deviceId.Trim();
        var session = USER_SESSION.Start(user.Id, resolvedDeviceId, deviceName?.Trim(), userAgent, ipAddress, clock);

        var (rawRefreshToken, refreshTokenHash) = refreshTokenGenerator.Generate();
        var refreshToken = REFRESH_TOKEN.Issue(user.Id, session.Id, refreshTokenHash, clock.UtcNow.Add(RefreshTokenLifetime));

        var (accessToken, accessTokenExpiresAtUtc) = accessTokenGenerator.Generate(user, session.Id);

        dbContext.UserSessions().Add(session);
        dbContext.RefreshTokens().Add(refreshToken);
        dbContext.SecurityAudits().Add(SECURITY_AUDIT.Record(auditEventType, user.Id, auditDetail, ipAddress, clock));

        var evictedSessionIds = await EnforceConcurrentSessionLimitAsync(user, session, ipAddress, cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Redis mirror: written only after the DB-authoritative save above succeeds, and in the same
        // order the DB now reflects (evicted sessions removed, the new one present).
        foreach (var evictedSessionId in evictedSessionIds)
        {
            await sessionRegistry.RemoveAsync(user.Id, evictedSessionId, cancellationToken).ConfigureAwait(false);
        }

        await sessionRegistry.RegisterAsync(user.Id, session.Id, RefreshTokenLifetime, cancellationToken).ConfigureAwait(false);

        return new LoginResult(accessToken, accessTokenExpiresAtUtc, rawRefreshToken, refreshToken.ExpiresAtUtc);
    }

    /// <summary>
    /// SE-03 enforcement: evicts the oldest session(s) over the effective limit, all staged on the
    /// shared <see cref="AppDbContext"/> (not saved here — <see cref="IssueAsync"/>'s own save covers
    /// this too). Returns the evicted sessions' ids so the caller can remove their Redis mirror keys
    /// once that save has actually committed.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> EnforceConcurrentSessionLimitAsync(
        USER user, USER_SESSION newSession, string? ipAddress, CancellationToken cancellationToken)
    {
        // newSession was already added to dbContext's change tracker by the caller but not yet saved,
        // so this query — translated to SQL against the DB as it currently stands — does not return
        // it. Combine it in explicitly ("including the one just created").
        var otherActiveSessions = await dbContext.UserSessions()
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var allActiveSessions = otherActiveSessions.Append(newSession).ToList();

        var effectiveLimit = user.MaxConcurrentSessionsOverride ?? concurrentSessionOptions.Value.MaxConcurrentSessions;

        var sessionsToEvict = ConcurrentSessionEvictionPolicy.SelectSessionsToEvict(allActiveSessions, effectiveLimit);
        if (sessionsToEvict.Count == 0)
        {
            return [];
        }

        var evictedSessionIds = sessionsToEvict.Select(s => s.Id).ToHashSet();

        // One query for every evicted session's still-active refresh token(s), not one query per
        // session (database.md: "ห้าม N+1").
        var tokensOfEvictedSessions = await dbContext.RefreshTokens()
            .Where(t => evictedSessionIds.Contains(t.SessionId) && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<Guid>(sessionsToEvict.Count);

        foreach (var sessionToEvict in sessionsToEvict)
        {
            sessionToEvict.Revoke(ConcurrentSessionLimitRevokeReason, clock);

            foreach (var token in tokensOfEvictedSessions.Where(t => t.SessionId == sessionToEvict.Id))
            {
                // null replacedByTokenId: an outright revoke, not a rotation — same distinction
                // REFRESH_TOKEN.Revoke's own doc comment draws for Refresh's reuse-detection path.
                token.Revoke(null, clock);
            }

            // Detail is structured JSON — ids only, never a token value (security.md: never log a token).
            var detail =
                $$"""{"evictedSessionId":"{{sessionToEvict.Id}}","newSessionId":"{{newSession.Id}}","effectiveLimit":{{effectiveLimit}}}""";
            dbContext.SecurityAudits().Add(
                SECURITY_AUDIT.Record(ConcurrentSessionLimitEventType, user.Id, detail, ipAddress, clock));

            var bodyHtml = ConcurrentSessionEvictedEmailContent.Render(user.DisplayName, effectiveLimit);
            emailOutbox.Enqueue(user.Email, ConcurrentSessionEvictedEmailContent.Subject, bodyHtml, ConcurrentSessionEvictedEmailContent.TemplateKey);

            result.Add(sessionToEvict.Id);

            logger.LogInformation(
                "SE-03: evicted session {EvictedSessionId} for user {UserId} (effective limit {EffectiveLimit}) due to new login {NewSessionId}.",
                sessionToEvict.Id,
                user.Id,
                effectiveLimit,
                newSession.Id);
        }

        return result;
    }
}
