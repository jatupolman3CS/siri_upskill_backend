using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Login;

/// <summary>
/// Verifies an email/password pair and, on success, starts a new <see cref="UserSession"/> +
/// <see cref="RefreshToken"/> + access token for it.
/// <para>
/// <b>Anti user-enumeration</b> (security.md's spirit, and P0-15's Register/ConfirmEmail set the bar
/// this task is held to — task instruction: "verify this holds by reading your own code before
/// finishing"). An attacker who can tell "no such account" apart from "wrong password" apart from
/// "account exists but isn't active yet" can enumerate real accounts one guess at a time. So every
/// rejection branch below — email not found, password does not verify, and account status is not
/// <see cref="UserStatus.Active"/> (still <see cref="UserStatus.PendingEmailConfirmation"/>, or
/// <see cref="UserStatus.Suspended"/>/<see cref="UserStatus.Deleted"/>) — returns the exact same
/// <see cref="InvalidCredentialsError"/>, never a distinguishable one.
/// </para>
/// <para>
/// <b>Timing parity</b> (task instruction, same spirit as Register's <c>BurnPasswordHashTime</c>): the
/// genuinely-wrong-password branch's dominant cost is one PBKDF2 verification
/// (<see cref="IUserPasswordHasher.VerifyPassword"/>). If the email simply doesn't exist, skipping
/// straight to <see cref="InvalidCredentialsError"/> would make that branch measurably faster —
/// letting a timing side-channel answer "does this email exist?" even though the *response body* never
/// does. So the not-found branch still runs one PBKDF2 verification, against
/// <see cref="DummyPasswordHash"/> (a hash of a fixed, made-up password — computed once, hardcoded,
/// never a real account's hash) instead of skipping it. This equalizes the dominant cost, not every
/// last millisecond (the real-user branch also does extra DB writes) — same residual, minor risk
/// Register's handler already documents.
/// </para>
/// <para>
/// On success: <see cref="User.RecordLogin"/>, a new <see cref="UserSession"/>
/// (<see cref="UserSession.Start"/>) capturing device/UA/IP, a new <see cref="RefreshToken"/>
/// (<see cref="RefreshToken.Issue"/>) linked to that session via its raw-token-hash pair from
/// <see cref="ISecurityTokenGenerator"/> (the exact same "generate random, hash before storing"
/// pattern P0-15 already established for <see cref="UserSecurityToken"/> — reused here rather than
/// inventing a second one, per docs/DATABASE.md's "RefreshTokens.TokenHash" being documented the same
/// way), a new access token (<see cref="IAccessTokenGenerator"/>), and a <see cref="SecurityAudit"/>
/// entry — all added to the same <see cref="AppDbContext"/> and committed in one
/// <see cref="AppDbContext.SaveChangesAsync"/> call (database.md: atomic multi-table writes).
/// </para>
/// <para>
/// <b>SE-03 concurrent-session enforcement</b> (P0-17, security.md §2/ARCHITECTURE.md §5) also runs
/// here, after the new session/refresh-token/audit rows above are staged but before that same
/// <see cref="AppDbContext.SaveChangesAsync"/> call: <see cref="EnforceConcurrentSessionLimitAsync"/>
/// loads every other currently-active <see cref="UserSession"/> for this user, combines them with the
/// one just created, and — via the pure <see cref="ConcurrentSessionEvictionPolicy"/> — decides which
/// (if any) must be evicted to stay within the effective limit
/// (<see cref="User.MaxConcurrentSessionsOverride"/> if set, else
/// <see cref="ConcurrentSessionOptions.MaxConcurrentSessions"/>). Each eviction revokes the session +
/// its still-active refresh token(s), writes a distinct <see cref="SecurityAudit"/> row, and queues a
/// "signed out on another device" email — all staged on the very same <see cref="AppDbContext"/>
/// instance, so eviction is atomic with the login it was triggered by (task instruction: one
/// <c>SaveChangesAsync</c> for the whole operation, not a separate one for eviction). Only once that
/// save succeeds does this handler touch Redis (<see cref="ISessionRegistry"/>) — see this class's
/// "Redis mirror" note further down and <see cref="ISessionRegistry"/>'s own doc comment for why that
/// ordering matters and why a Redis failure there must never undo/fail the login that already
/// committed.
/// </para>
/// </summary>
public sealed class LoginHandler(
    AppDbContext dbContext,
    IUserPasswordHasher passwordHasher,
    ISecurityTokenGenerator refreshTokenGenerator,
    IAccessTokenGenerator accessTokenGenerator,
    IClock clock,
    IOptions<ConcurrentSessionOptions> concurrentSessionOptions,
    ISessionRegistry sessionRegistry,
    IEmailOutbox emailOutbox,
    ILogger<LoginHandler> logger)
{
    /// <summary>30 days per security.md ("refresh token 30 วัน แบบ rotation + reuse detection") — also
    /// the Redis mirror's TTL (ARCHITECTURE.md §5: "session:{userId}:{sessionId} (TTL = refresh
    /// token)"), reused as the exact same constant rather than a second copy that could drift.</summary>
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    /// <summary><see cref="UserSession.RevokeReason"/> for an SE-03 eviction — distinct from
    /// Refresh's "suspected_refresh_token_reuse" so <c>UserSessions</c> rows are self-explanatory
    /// without joining out to <see cref="SecurityAudit"/>.</summary>
    private const string ConcurrentSessionLimitRevokeReason = "concurrent_session_limit_exceeded";

    /// <summary><see cref="SecurityAudit.EventType"/> for an SE-03 eviction — distinct from plain
    /// "login.succeeded" (this handler's own normal-path audit entry) and Refresh's
    /// "refresh_token.reuse_detected", so this specific event is filterable in <c>SecurityAudits</c>.</summary>
    private const string ConcurrentSessionLimitEventType = "session.evicted_concurrent_limit";

    private static readonly DomainError InvalidCredentialsError =
        DomainError.Validation("อีเมลหรือรหัสผ่านไม่ถูกต้อง");

    /// <summary>
    /// A real PBKDF2 hash (ASP.NET Core Identity's <c>PasswordHasher&lt;TUser&gt;</c> format,
    /// precomputed once via the exact same hasher this handler uses — see the class doc comment's
    /// "Timing parity" section) of a fixed, made-up password that was never assigned to any account.
    /// Verifying against this — rather than hashing the caller's password from scratch each time —
    /// still costs one real PBKDF2 verification, it's just against a constant instead of a per-call
    /// throwaway hash. Either approach burns equivalent time; a fixed constant is what the task
    /// instructions ask for here.
    /// <para>
    /// <b>Important:</b> <c>VerifyHashedPassword</c> reads the iteration count embedded in the hash
    /// itself, not <see cref="UserPasswordHasher.IterationCount"/>'s current value — so this constant
    /// is only timing-equivalent to a real verification as long as it was generated with the SAME
    /// iteration count real hashes are currently created with
    /// (<see cref="UserPasswordHasher.IterationCount"/>). This value was regenerated to match when
    /// that constant was raised from the framework's 100,000 default to 600,000 per SECURITY.md — if
    /// <see cref="UserPasswordHasher.IterationCount"/> ever changes again, this must be regenerated
    /// too, or the not-found branch quietly becomes faster than the wrong-password branch again and
    /// the timing side-channel this exists to close reopens.
    /// </para>
    /// </summary>
    private const string DummyPasswordHash =
        "AQAAAAIACSfAAAAAEFSMVvnzLlSUfEo52buqLNyK8ayqu4W6kpNnJ2VHxT+GuZORErqK0KNVGkN5IP6PkA==";

    public async Task<Result<LoginResult>> HandleAsync(
        LoginCommand command,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = command.Email.Trim().ToUpperInvariant();

        var user = await dbContext.Users()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            VerifyAgainstDummyHash(command.Password);
            return Result.Failure<LoginResult>(InvalidCredentialsError);
        }

        if (passwordHasher.VerifyPassword(user, user.PasswordHash, command.Password) == PasswordVerificationResult.Failed)
        {
            return Result.Failure<LoginResult>(InvalidCredentialsError);
        }

        if (user.Status != UserStatus.Active)
        {
            // Same generic error as a wrong password — never reveal PendingEmailConfirmation/
            // Suspended/Deleted to an unauthenticated caller (task instruction).
            return Result.Failure<LoginResult>(InvalidCredentialsError);
        }

        user.RecordLogin(clock);

        var deviceId = string.IsNullOrWhiteSpace(command.DeviceId) ? Guid.NewGuid().ToString() : command.DeviceId.Trim();
        var session = UserSession.Start(user.Id, deviceId, command.DeviceName?.Trim(), userAgent, ipAddress, clock);

        var (rawRefreshToken, refreshTokenHash) = refreshTokenGenerator.Generate();
        var refreshToken = RefreshToken.Issue(user.Id, session.Id, refreshTokenHash, clock.UtcNow.Add(RefreshTokenLifetime));

        var (accessToken, accessTokenExpiresAtUtc) = accessTokenGenerator.Generate(user, session.Id);

        dbContext.UserSessions().Add(session);
        dbContext.RefreshTokens().Add(refreshToken);
        dbContext.SecurityAudits().Add(SecurityAudit.Record("login.succeeded", user.Id, null, ipAddress, clock));

        var evictedSessionIds = await EnforceConcurrentSessionLimitAsync(user, session, ipAddress, cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Redis mirror: written only after the DB-authoritative save above succeeds, and in the same
        // order the DB now reflects (evicted sessions removed, the new one present) — see
        // ISessionRegistry's doc comment for why MSSQL, not Redis, is authoritative for this task, and
        // the task instruction this satisfies: mirroring the new session right after the enforcement
        // check so an evicted session that gets immediately retried doesn't leave stale Redis state
        // (by the time of any retry, both this removal and the registration below have already run).
        foreach (var evictedSessionId in evictedSessionIds)
        {
            await sessionRegistry.RemoveAsync(user.Id, evictedSessionId, cancellationToken).ConfigureAwait(false);
        }

        await sessionRegistry.RegisterAsync(user.Id, session.Id, RefreshTokenLifetime, cancellationToken).ConfigureAwait(false);

        return new LoginResult(accessToken, accessTokenExpiresAtUtc, rawRefreshToken, refreshToken.ExpiresAtUtc);
    }

    private void VerifyAgainstDummyHash(string password)
    {
        var dummyUser = User.Register("dummy@example.invalid", "DUMMY@EXAMPLE.INVALID", "placeholder", "placeholder");
        passwordHasher.VerifyPassword(dummyUser, DummyPasswordHash, password);
    }

    /// <summary>
    /// SE-03 enforcement: evicts the oldest session(s) over the effective limit, all staged on the
    /// caller's <see cref="AppDbContext"/> (not saved here — the caller's own
    /// <see cref="AppDbContext.SaveChangesAsync"/> call covers this too, task instruction: one atomic
    /// save for the whole login+eviction operation). Returns the evicted sessions' ids so the caller
    /// can remove their Redis mirror keys once that save has actually committed.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> EnforceConcurrentSessionLimitAsync(
        User user, UserSession newSession, string? ipAddress, CancellationToken cancellationToken)
    {
        // newSession was already added to dbContext's change tracker by the caller but not yet saved,
        // so this query — translated to SQL against the DB as it currently stands — does not return
        // it. Combine it in explicitly (task instruction: "including the one just created").
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
                // RefreshToken.Revoke's own doc comment draws for Refresh's reuse-detection path.
                token.Revoke(null, clock);
            }

            // Detail is structured JSON, same convention as Refresh's reuse-detection audit entry —
            // ids only, never a token value (security.md: never log a token).
            var detail =
                $$"""{"evictedSessionId":"{{sessionToEvict.Id}}","newSessionId":"{{newSession.Id}}","effectiveLimit":{{effectiveLimit}}}""";
            dbContext.SecurityAudits().Add(
                SecurityAudit.Record(ConcurrentSessionLimitEventType, user.Id, detail, ipAddress, clock));

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
