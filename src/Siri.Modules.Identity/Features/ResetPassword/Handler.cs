using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.ResetPassword;

/// <summary>
/// Redeems a raw password-reset token: hashes it the same way <c>ForgotPasswordHandler</c> hashed it at
/// issuance, looks the row up by that hash, and — only if it is found, unexpired, unconsumed, and still
/// points at an <see cref="UserStatus.Active"/> user — changes the password, consumes the token, and (the
/// real security decision this task makes — see below) revokes every currently-active
/// <see cref="UserSession"/>/<see cref="RefreshToken"/> the account has.
/// <para>
/// <b>Anti-enumeration parallel to ConfirmEmail</b> (security.md's spirit). Every rejection path below —
/// not found, expired, already consumed, or (defensively; unlike ConfirmEmail's "should be unreachable
/// in practice" case, this one genuinely can happen if an admin suspends/deletes the account in the
/// window between requesting and redeeming a reset link) a user no longer <see cref="UserStatus.Active"/>
/// — returns the exact same <see cref="InvalidTokenError"/>, never a distinguishable one.
/// </para>
/// <para>
/// <b>Design decision — successful reset revokes every active session/refresh token for the account</b>
/// (task instruction: "this is a genuine security decision, not a formality"). Someone who goes through
/// the "forgot password" flow is very often doing so specifically *because* they suspect their account
/// is compromised (a stranger changed the password, or the owner simply wants to lock out a device they
/// no longer trust) — leaving every already-issued session/refresh token alive across a reset would
/// completely defeat that purpose: whoever holds an old session could carry on using the account right
/// through the reset. Industry practice (OWASP's authentication cheat sheet, and every mainstream
/// consumer platform) treats "invalidate all other sessions on password change" as the default, not an
/// opt-in extra, so this handler does the same unconditionally — there is no "keep me logged in
/// elsewhere" escape hatch, because the whole point of this task's threat model is that the caller
/// cannot be assumed to know which sessions (if any) are still trustworthy. Implemented by reusing the
/// exact same revoke methods and Redis-mirror cleanup <c>Login.LoginHandler</c>'s SE-03 eviction path and
/// <c>Refresh.RefreshHandler</c>'s reuse-detection path already established — <see cref="UserSession.Revoke"/>/
/// <see cref="RefreshToken.Revoke"/> staged on this same <see cref="AppDbContext"/>, then
/// <see cref="ISessionRegistry.RemoveAsync"/> called once per revoked session only *after* that save has
/// committed (same ordering/fail-open reasoning <see cref="ISessionRegistry"/>'s own doc comment gives —
/// a Redis hiccup here must never undo or fail a password reset that already succeeded in the
/// database-of-record). A distinct <see cref="SecurityAudit"/> row (<c>EventType</c> =
/// "password.reset_succeeded", not reused from any existing event type) is written so this specific
/// event is filterable in <c>SecurityAudits</c> later, same reasoning
/// <c>LoginHandler</c>'s SE-03 eviction audit entry and <c>RefreshHandler</c>'s reuse-detection audit
/// entry already establish for their own distinct events.
/// </para>
/// <para>
/// <b>Design decision — also send a "your password was changed" notification email</b> (task
/// instruction: "consider whether ... make a decision and implement or explicitly skip it with
/// reasoning"). Implemented: queued via the same <see cref="IEmailOutbox"/> mechanism as every other
/// email in this module, staged on this same <see cref="AppDbContext"/> so it commits atomically with
/// the password change itself. This is a standard security-notification practice (and the direct
/// counterpart to <c>ConcurrentSessionEvictedEmailContent</c>, P0-17's "you were signed out elsewhere"
/// notice) specifically because it is the one signal that reaches an account owner who did
/// <em>not</em> request the reset themselves — an attacker who somehow obtained a valid reset link
/// (e.g. from a compromised inbox) and used it would otherwise leave the real owner with no indication
/// anything happened until they next tried, and failed, to log in with their old password.
/// </para>
/// <para>
/// On success: <see cref="UserPasswordHasher.HashPassword"/> computes the new hash,
/// <see cref="User.ChangePassword"/> applies it, the reset token is
/// <see cref="UserSecurityToken.Consume"/>d, every active session/refresh-token is revoked, the audit
/// row is written, and the notification email is queued — all added to the same <see cref="AppDbContext"/>
/// and committed in exactly one <see cref="AppDbContext.SaveChangesAsync"/> call (database.md: atomic
/// multi-table writes; task instruction: "persist everything atomically in one SaveChangesAsync").
/// </para>
/// </summary>
public sealed class ResetPasswordHandler(
    AppDbContext dbContext,
    ISecurityTokenGenerator tokenGenerator,
    IUserPasswordHasher passwordHasher,
    IClock clock,
    ISessionRegistry sessionRegistry,
    IEmailOutbox emailOutbox,
    ILogger<ResetPasswordHandler> logger)
{
    /// <summary><see cref="UserSession.RevokeReason"/> for a password-reset-triggered revocation —
    /// distinct from SE-03's "concurrent_session_limit_exceeded" and Refresh's
    /// "suspected_refresh_token_reuse" so <c>UserSessions</c> rows stay self-explanatory without joining
    /// out to <see cref="SecurityAudit"/>.</summary>
    private const string PasswordResetRevokeReason = "password_reset";

    /// <summary><see cref="SecurityAudit.EventType"/> for a successful reset — distinct from every other
    /// event type already in use in this codebase (see the class doc comment's design-decision section).</summary>
    private const string PasswordResetEventType = "password.reset_succeeded";

    /// <summary>Deliberately identical whether the token was never issued, already used, expired, or
    /// belongs to a since-deactivated user — see the class doc comment's anti-enumeration section.</summary>
    private static readonly DomainError InvalidTokenError =
        DomainError.Validation("ลิงก์ตั้งรหัสผ่านใหม่ไม่ถูกต้องหรือหมดอายุแล้ว กรุณาขอลิงก์ตั้งรหัสผ่านใหม่อีกครั้ง");

    private const string SuccessMessage =
        "ตั้งรหัสผ่านใหม่เรียบร้อยแล้ว คุณสามารถเข้าสู่ระบบด้วยรหัสผ่านใหม่ได้ทันที " +
        "เพื่อความปลอดภัย ทุกอุปกรณ์ที่เคยเข้าสู่ระบบไว้ก่อนหน้านี้ถูกออกจากระบบแล้ว";

    public async Task<Result<ResetPasswordResponse>> HandleAsync(
        ResetPasswordCommand command, string? ipAddress, CancellationToken cancellationToken)
    {
        var tokenHash = tokenGenerator.Hash(command.Token);

        var securityToken = await dbContext.UserSecurityTokens()
            .FirstOrDefaultAsync(
                t => t.TokenHash == tokenHash && t.Purpose == UserSecurityTokenPurpose.PasswordReset,
                cancellationToken)
            .ConfigureAwait(false);

        if (securityToken is null || !securityToken.IsValid(clock))
        {
            return Result.Failure<ResetPasswordResponse>(InvalidTokenError);
        }

        var user = await dbContext.Users()
            .FirstOrDefaultAsync(u => u.Id == securityToken.UserId, cancellationToken)
            .ConfigureAwait(false);

        // Unlike ConfirmEmail's equivalent check (documented there as "should be unreachable in
        // practice"), this one genuinely can trigger: an admin can suspend/delete an account in the
        // window between a reset link being issued and it being redeemed. Mapped to the same generic
        // rejection either way, never a distinguishable one (class doc comment's anti-enumeration
        // section).
        if (user is null || user.Status != UserStatus.Active)
        {
            return Result.Failure<ResetPasswordResponse>(InvalidTokenError);
        }

        var newPasswordHash = passwordHasher.HashPassword(user, command.NewPassword);
        user.ChangePassword(newPasswordHash);
        securityToken.Consume(clock);

        dbContext.SecurityAudits().Add(SecurityAudit.Record(PasswordResetEventType, user.Id, null, ipAddress, clock));

        var revokedSessionIds = await RevokeAllActiveSessionsAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var bodyHtml = PasswordChangedEmailContent.Render(user.DisplayName);
        emailOutbox.Enqueue(user.Email, PasswordChangedEmailContent.Subject, bodyHtml, PasswordChangedEmailContent.TemplateKey);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Redis mirror: only after the DB-authoritative save above succeeds — same ordering/fail-open
        // reasoning as Login's SE-03 eviction path and Refresh's reuse-detection path.
        foreach (var sessionId in revokedSessionIds)
        {
            await sessionRegistry.RemoveAsync(user.Id, sessionId, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "ResetPassword: user {UserId} reset their password; revoked {RevokedSessionCount} active session(s).",
            user.Id,
            revokedSessionIds.Count);

        return new ResetPasswordResponse(SuccessMessage);
    }

    /// <summary>
    /// Revokes every currently-active <see cref="UserSession"/> for <paramref name="userId"/> and every
    /// still-active <see cref="RefreshToken"/> belonging to those sessions, all staged on the caller's
    /// <see cref="AppDbContext"/> (not saved here — the caller's own <see cref="AppDbContext.SaveChangesAsync"/>
    /// call covers this too, same "one atomic save for the whole operation" shape
    /// <c>LoginHandler.EnforceConcurrentSessionLimitAsync</c> already uses). Returns the revoked
    /// sessions' ids so the caller can clean up their Redis mirror keys once that save has committed.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> RevokeAllActiveSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var activeSessions = await dbContext.UserSessions()
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activeSessions.Count == 0)
        {
            return [];
        }

        var activeSessionIds = activeSessions.Select(s => s.Id).ToHashSet();

        // One query for every active session's still-active refresh token(s), not one query per
        // session (database.md: "ห้าม N+1") — same pattern LoginHandler's own eviction path uses.
        var activeTokens = await dbContext.RefreshTokens()
            .Where(t => activeSessionIds.Contains(t.SessionId) && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var session in activeSessions)
        {
            session.Revoke(PasswordResetRevokeReason, clock);
        }

        foreach (var token in activeTokens)
        {
            // null replacedByTokenId: an outright revoke, not a rotation — same distinction
            // RefreshToken.Revoke's own doc comment draws for Refresh's reuse-detection path.
            token.Revoke(null, clock);
        }

        return activeSessions.Select(s => s.Id).ToList();
    }
}
