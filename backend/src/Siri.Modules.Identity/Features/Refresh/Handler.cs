using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Refresh;

/// <summary>
/// Redeems a raw refresh token: hashes it the same way <c>Login.LoginHandler</c> hashed the one it
/// issued, looks the row up by that hash, and — depending on what it finds — either rotates it into a
/// fresh token (the normal path), rejects it, or, if the token was already rotated away once before,
/// treats presenting it again as a theft signal and revokes the whole session's token family.
/// <para>
/// <b>Rotation with reuse detection</b> (security.md: "refresh token 30 วัน แบบ rotation + reuse
/// detection (ถ้าเจอ token เก่าถูกใช้ซ้ำ = revoke ทั้ง family)"). Every refresh call presents exactly
/// one refresh token and, if it's valid, walks away with a brand-new one — the old one is revoked in
/// the same instant it's used (<see cref="RefreshToken.Revoke"/>, called by
/// <see cref="HandleAsync"/>'s normal-path branch below). That single fact is what makes reuse
/// detectable at all: a legitimate client only ever has <em>one</em> valid refresh token for its
/// session at a time, so if a token that is already revoked shows up again, one of exactly two things
/// happened — the legitimate client retried after a network race (rare, and the failure mode is
/// merely an extra login), or someone else got hold of an old token value (a real compromise, e.g.
/// from a log leak, XSS reading a cookie it shouldn't have been able to, or a stolen device backup).
/// Security.md's answer to that ambiguity is to always assume the worse case and revoke the entire
/// family — a legitimate client that got caught in the false-positive case just has to log in again,
/// which is a small, safe inconvenience; silently ignoring the reuse would let a real attacker keep a
/// stolen token family alive indefinitely.
/// </para>
/// <para>
/// <b>How the "family" is identified</b> (a real design decision, not a trivial lookup — the task
/// instructions specifically call this out). <see cref="RefreshToken.ReplacedByTokenId"/> lets you walk
/// the chain <em>forward</em> from any one token, but reuse detection needs the <em>whole</em> family
/// from wherever it started, and the reused token itself is (by definition) not the newest link in
/// that chain. Walking backward would need a "replaced-from" pointer this schema doesn't have. Instead,
/// this handler uses <see cref="RefreshToken.SessionId"/>: a <see cref="UserSession"/> is created
/// exactly once, at login (<c>Login.LoginHandler</c>: <c>UserSession.Start</c>), and every single
/// rotation in this handler's normal-path branch below issues the replacement token with
/// <c>token.SessionId</c> — the <em>same</em> session id the token being rotated already carried,
/// never a new one. That means every refresh token that has ever existed for one login's lifetime,
/// from the very first one <c>LoginHandler</c> issued through however many rotations have happened
/// since, shares one <see cref="RefreshToken.SessionId"/> value by construction. So "every row in
/// <c>RefreshTokens</c> with this <see cref="RefreshToken.SessionId"/>" is not merely a practical
/// approximation of the family — given how this handler and <c>LoginHandler</c> actually issue tokens,
/// it <em>is</em> the family, with no separate bookkeeping needed. (Walking <see cref="RefreshToken.ReplacedByTokenId"/>
/// forward from the reused token to the current tip would reach the same currently-active row, but it
/// still needs a starting point, this schema has no "replaced-from"/earliest-token pointer to jump to
/// one directly, and — since only the chain's tip is ever unrevoked at a time — one indexed
/// <see cref="RefreshToken.SessionId"/> query already returns exactly that same row set in one
/// round-trip instead of N.)
/// </para>
/// <para>
/// On detected reuse: every currently-active (<see cref="RefreshToken.RevokedAtUtc"/> still
/// <c>null</c>) token in the family is revoked, the <see cref="UserSession"/> itself is revoked (so a
/// still-valid access token from that session can't be used to silently mint yet another refresh — a
/// later authorization-policy task, P0-22, is expected to also check session liveness on protected
/// requests), and a <see cref="SecurityAudit"/> row is written with a distinct
/// <c>EventType</c> ("refresh_token.reuse_detected", vs. plain "login.succeeded"/normal rotation which
/// writes none) specifically so this is visible to whoever reviews <c>SecurityAudits</c> later. The
/// caller still only ever sees the same generic <see cref="InvalidRefreshTokenError"/> either way —
/// telling an attacker "we caught you" would only teach them to be quieter next time.
/// </para>
/// <para>
/// Normal path (valid, unexpired, not previously revoked): revoke the presented token
/// (<c>ReplacedByTokenId</c> set to the new token's id), issue the new one, issue a new access token,
/// all added to the same <see cref="AppDbContext"/> and committed in exactly one
/// <see cref="AppDbContext.SaveChangesAsync"/> call — so a failure partway through cannot leave the
/// old token revoked with no working replacement ever persisted (task instruction: this must not be
/// able to lock the user out).
/// </para>
/// </summary>
public sealed class RefreshHandler(
    AppDbContext dbContext,
    ISecurityTokenGenerator refreshTokenGenerator,
    IAccessTokenGenerator accessTokenGenerator,
    IClock clock,
    ISessionRegistry sessionRegistry,
    ILogger<RefreshHandler> logger)
{
    /// <summary>30 days per security.md — same lifetime <c>Login.LoginHandler</c> uses for the first
    /// token in a session, so a full rotation chain never silently grants a longer-lived session than
    /// the original login did.</summary>
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    /// <summary>Deliberately identical whether the token was never valid, already used, expired, or
    /// belongs to a since-deactivated user — see the class doc comment's reuse-detection section for
    /// why a caller must never be able to tell these apart.</summary>
    private static readonly DomainError InvalidRefreshTokenError =
        DomainError.Validation("เซสชันหมดอายุหรือไม่ถูกต้อง กรุณาเข้าสู่ระบบใหม่อีกครั้ง");

    public async Task<Result<RefreshResult>> HandleAsync(RefreshCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RawRefreshToken))
        {
            return Result.Failure<RefreshResult>(InvalidRefreshTokenError);
        }

        var tokenHash = refreshTokenGenerator.Hash(command.RawRefreshToken);

        var presentedToken = await dbContext.RefreshTokens()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (presentedToken is null)
        {
            return Result.Failure<RefreshResult>(InvalidRefreshTokenError);
        }

        if (presentedToken.RevokedAtUtc is not null)
        {
            await RevokeTokenFamilyAsync(presentedToken, command.IpAddress, cancellationToken).ConfigureAwait(false);
            return Result.Failure<RefreshResult>(InvalidRefreshTokenError);
        }

        if (presentedToken.ExpiresAtUtc <= clock.UtcNow)
        {
            return Result.Failure<RefreshResult>(InvalidRefreshTokenError);
        }

        var user = await dbContext.Users()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == presentedToken.UserId, cancellationToken)
            .ConfigureAwait(false);

        // Neither branch below should be reachable in practice (RefreshTokens.UserId is a Restrict FK
        // to Users, and nothing deactivates a user mid-token-lifetime today) — checked anyway, and
        // mapped to the same generic rejection, never a distinguishable one.
        if (user is null || user.Status != UserStatus.Active)
        {
            return Result.Failure<RefreshResult>(InvalidRefreshTokenError);
        }

        var session = await dbContext.UserSessions()
            .FirstOrDefaultAsync(s => s.Id == presentedToken.SessionId, cancellationToken)
            .ConfigureAwait(false);
        session?.Touch(clock);

        var (rawNewRefreshToken, newRefreshTokenHash) = refreshTokenGenerator.Generate();
        var newRefreshToken = RefreshToken.Issue(
            user.Id,
            presentedToken.SessionId, // same session id all the way through — see class doc comment
            newRefreshTokenHash,
            clock.UtcNow.Add(RefreshTokenLifetime));

        dbContext.RefreshTokens().Add(newRefreshToken);
        presentedToken.Revoke(newRefreshToken.Id, clock);

        var (accessToken, accessTokenExpiresAtUtc) = accessTokenGenerator.Generate(user, presentedToken.SessionId);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RefreshResult(accessToken, accessTokenExpiresAtUtc, rawNewRefreshToken, newRefreshToken.ExpiresAtUtc);
    }

    private async Task RevokeTokenFamilyAsync(RefreshToken reusedToken, string? ipAddress, CancellationToken cancellationToken)
    {
        var activeFamilyTokens = await dbContext.RefreshTokens()
            .Where(t => t.SessionId == reusedToken.SessionId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var familyToken in activeFamilyTokens)
        {
            familyToken.Revoke(null, clock);
        }

        var session = await dbContext.UserSessions()
            .FirstOrDefaultAsync(s => s.Id == reusedToken.SessionId, cancellationToken)
            .ConfigureAwait(false);
        session?.Revoke("suspected_refresh_token_reuse", clock);

        // Detail is structured JSON (SecurityAudits.Detail's documented shape) — session/token ids
        // only, never the token value itself (security.md: never log a token).
        var detail = $$"""{"sessionId":"{{reusedToken.SessionId}}","reusedRefreshTokenId":"{{reusedToken.Id}}"}""";
        dbContext.SecurityAudits().Add(
            SecurityAudit.Record("refresh_token.reuse_detected", reusedToken.UserId, detail, ipAddress, clock));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // P0-17: keep the Redis mirror consistent with this revocation path too, not just SE-03's own
        // eviction (task instruction — "check what P0-16 built and extend it consistently rather than
        // leaving a gap"). Only meaningful if `session` was actually found (see the `session?.Revoke`
        // null-conditional above — the same "should be unreachable in practice" case
        // HandleAsync's user-lookup comment already notes); best-effort/fail-open, after the
        // DB-authoritative save above, same reasoning as Login's own Redis mirroring.
        if (session is not null)
        {
            await sessionRegistry.RemoveAsync(session.UserId, session.Id, cancellationToken).ConfigureAwait(false);
        }

        logger.LogWarning(
            "Refresh token reuse detected for session {SessionId} — revoked {Count} active token(s) and the session.",
            reusedToken.SessionId,
            activeFamilyTokens.Count);
    }
}
