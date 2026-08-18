using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.RevokeSession;

/// <summary>
/// Revokes exactly one of the authenticated caller's OWN <see cref="UserSession"/>s and every still-
/// active <see cref="RefreshToken"/> issued to it — the "ถอดอุปกรณ์รายตัว" half of P0-18
/// (docs/TASKS.md), backing docs/SECURITY.md §2's "ผู้ใช้...ถอดอุปกรณ์เองได้ที่หน้า 'อุปกรณ์ที่เข้าสู่
/// ระบบ'".
/// <para>
/// <b>Ownership check — the single most important thing in this handler</b> (task instruction: treat
/// this with the same weight P0-17's verify pass gave "does eviction actually revoke the refresh token
/// too"). A session id is an ordinary, guessable-format (UUIDv7 is time-ordered, not secret) route
/// value — nothing about it proves the caller owns the row it names. So the very first thing
/// <see cref="HandleAsync"/> does is look the session up scoped to BOTH
/// <see cref="RevokeSessionCommand.SessionId"/> AND <see cref="RevokeSessionCommand.UserId"/> in the
/// same query — not "load by id, then separately check <c>session.UserId == command.UserId</c>". The
/// two are logically equivalent, but filtering ownership into the WHERE clause up front means there is
/// no code path where a session row belonging to someone else is ever materialized into memory at all,
/// let alone accidentally acted upon by a later refactor that forgets a separate check. If the query
/// finds nothing — because the id genuinely does not exist, or names a real session belonging to a
/// different account, or names the caller's own session but it was already revoked — every one of those
/// three cases returns the exact same <see cref="NotFoundError"/> (task instruction: "return the same
/// not-found-style response either way ... that distinction itself would leak information about which
/// session IDs are valid for other accounts").
/// </para>
/// <para>
/// <b>Design decision — a caller MAY revoke their own current session</b> (task instruction: "make and
/// document a specific decision"). Allowed, unconditionally: it is the caller's own device and their own
/// choice, no different in kind from an ordinary "log out" action, and there is no security reason to
/// special-case it. The one real consequence — the caller's own access token, being a stateless JWT,
/// remains individually valid for whatever remains of its ~15-minute lifetime even after the session row
/// it was minted from is revoked (nothing in this codebase checks session liveness on every protected
/// request; <c>Refresh.RefreshHandler</c>'s own doc comment already names this as a known, accepted gap
/// for a later authorization task, not something this handler introduces) — is an already-existing,
/// already-accepted tradeoff, not a new one this decision creates.
/// <see cref="RevokeSessionResponse.WasCurrentSession"/> tells the frontend when this happened so it can
/// still drop its own in-memory token immediately rather than rely on the token to simply expire.
/// </para>
/// <para>
/// On success: <see cref="UserSession.Revoke"/>, every still-active <see cref="RefreshToken"/> for that
/// session revoked the same way <c>ResetPasswordHandler</c>'s bulk revoke does, a distinct
/// <see cref="SecurityAudit"/> row (<c>EventType</c> = <see cref="RevokedByUserEventType"/> — the task
/// instruction's own suggested name, distinct from SE-03 eviction/ResetPassword's bulk revoke/Refresh's
/// reuse-detection events), all in one <see cref="AppDbContext.SaveChangesAsync"/> call — then, only
/// after that commit succeeds, the Redis mirror key is removed (<see cref="ISessionRegistry.RemoveAsync"/>),
/// the exact same post-commit, fail-open ordering every other revocation path in this module already
/// established (Login's SE-03 eviction, Refresh's reuse-detection, ResetPassword's bulk revoke).
/// </para>
/// </summary>
public sealed class RevokeSessionHandler(
    AppDbContext dbContext,
    IClock clock,
    ISessionRegistry sessionRegistry,
    ILogger<RevokeSessionHandler> logger)
{
    /// <summary><see cref="UserSession.RevokeReason"/> for this handler's own path — distinct from
    /// SE-03's "concurrent_session_limit_exceeded", Refresh's "suspected_refresh_token_reuse", and
    /// ResetPassword's "password_reset", so <c>UserSessions</c> rows stay self-explanatory without
    /// joining out to <see cref="SecurityAudit"/>.</summary>
    private const string RevokeReasonValue = "revoked_by_user";

    /// <summary><see cref="SecurityAudit.EventType"/> for a successful single-session revoke (task
    /// instruction's own suggested name).</summary>
    private const string RevokedByUserEventType = "session.revoked_by_user";

    /// <summary>Deliberately identical whether the id doesn't exist, belongs to another account, or was
    /// already revoked — see the class doc comment's ownership-check section. Says nothing about
    /// ownership either way, on purpose.</summary>
    private static readonly DomainError NotFoundError =
        DomainError.NotFound("ไม่พบอุปกรณ์นี้ หรือถูกถอดออกจากระบบไปแล้ว");

    private const string SuccessMessage = "ถอดอุปกรณ์นี้ออกจากระบบเรียบร้อยแล้ว";

    public async Task<Result<RevokeSessionResponse>> HandleAsync(
        RevokeSessionCommand command, string? ipAddress, CancellationToken cancellationToken)
    {
        // Ownership check baked directly into the query — see this class's doc comment.
        var session = await dbContext.UserSessions()
            .FirstOrDefaultAsync(
                s => s.Id == command.SessionId && s.UserId == command.UserId && s.RevokedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return Result.Failure<RevokeSessionResponse>(NotFoundError);
        }

        var wasCurrentSession = command.CurrentSessionId is not null && session.Id == command.CurrentSessionId;

        var activeTokens = await dbContext.RefreshTokens()
            .Where(t => t.SessionId == session.Id && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        session.Revoke(RevokeReasonValue, clock);

        foreach (var token in activeTokens)
        {
            // null replacedByTokenId: an outright revoke, not a rotation — same distinction
            // RefreshToken.Revoke's own doc comment draws for Refresh's reuse-detection path.
            token.Revoke(null, clock);
        }

        // Detail is structured JSON, same convention as Login's SE-03 eviction audit entry — ids only,
        // never a token value (security.md: never log a token).
        var detail = $$"""{"revokedSessionId":"{{session.Id}}","wasCurrentSession":{{(wasCurrentSession ? "true" : "false")}}}""";
        dbContext.SecurityAudits().Add(SecurityAudit.Record(RevokedByUserEventType, command.UserId, detail, ipAddress, clock));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Redis mirror: only after the DB-authoritative save above succeeds — same ordering/fail-open
        // reasoning as every other revocation path in this module.
        await sessionRegistry.RemoveAsync(command.UserId, session.Id, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "RevokeSession: user {UserId} revoked their own session {SessionId} (current session: {WasCurrentSession}).",
            command.UserId,
            session.Id,
            wasCurrentSession);

        return new RevokeSessionResponse(SuccessMessage, wasCurrentSession);
    }
}
