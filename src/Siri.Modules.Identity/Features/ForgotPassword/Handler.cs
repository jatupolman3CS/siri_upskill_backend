using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.ForgotPassword;

/// <summary>
/// Starts the "forgot password" flow: if the (normalized) email belongs to a genuinely existing,
/// <see cref="UserStatus.Active"/> account, issues a <see cref="UserSecurityToken"/> (Purpose =
/// <see cref="UserSecurityTokenPurpose.PasswordReset"/>) and queues a reset-link email — otherwise does
/// neither and returns the exact same success response anyway. Never changes the password or touches
/// sessions itself; that only happens once the link is actually redeemed (<c>ResetPasswordHandler</c>).
/// <para>
/// <b>Anti user-enumeration</b> (security.md's spirit; the same bar <c>RegisterHandler</c>/
/// <c>LoginHandler</c> are held to — task instruction). Every rejection reason — no account with this
/// email, an account that exists but is not yet <see cref="UserStatus.Active"/> (still
/// <see cref="UserStatus.PendingEmailConfirmation"/>), or one that is <see cref="UserStatus.Suspended"/>/
/// <see cref="UserStatus.Deleted"/> — collapses into the exact same <see cref="ForgotPasswordResponse"/>
/// and the exact same 200 status as a genuine success. An attacker who could tell these apart could
/// enumerate real accounts one address at a time, exactly the risk Register/Login's own handlers guard
/// against.
/// </para>
/// <para>
/// <b>Timing parity — and why this is deliberately NOT a copy of Login's <c>DummyPasswordHash</c>
/// mechanism</b> (task instruction: "think about what the actual dominant cost is here"). Login's/
/// Register's timing side-channel exists because their dominant per-request cost is one PBKDF2
/// verification/hash — a primitive deliberately tuned to ~100ms+ of CPU time (600k iterations,
/// security.md) — so skipping it on the "doesn't exist" branch would make that branch measurably
/// faster, and burning an equivalent hash closes that gap. <b>This handler never hashes a password at
/// all</b> — there is nothing analogous to PBKDF2 here, so grafting <c>BurnPasswordHashTime</c>'s shape
/// onto this flow would not be "the equivalent thing done right", it would be adding a pointless PBKDF2
/// call to an operation that never needed one, which RegisterValidator's own doc comment already argues
/// against in spirit ("composition rules ... add needless cost with no matching security benefit").
/// <see cref="ISecurityTokenGenerator.Generate"/>'s actual cost (one CSPRNG read + one SHA-256 hash) is
/// itself microseconds — not remotely comparable to PBKDF2 — so there is no meaningful CPU-bound gap to
/// equalize on that side either.
/// <para>
/// The genuine (if much smaller) asymmetry that remains is on the exists-and-active branch's extra
/// database round trip(s): invalidating prior outstanding tokens, inserting the new
/// <see cref="UserSecurityToken"/> row, inserting the queued email, and committing them via
/// <see cref="AppDbContext.SaveChangesAsync"/> — none of which the not-found/not-active branch does.
/// This is the exact same category of residual gap <c>RegisterHandler</c>'s own doc comment already
/// names and explicitly accepts ("the new-user branch also does two extra DB writes ... full timing
/// parity down to the database round-trip is a deeper hardening exercise this task does not attempt;
/// noted as a residual, minor risk") — not a new one introduced here. Deliberately not "closed" with a
/// fabricated dummy write (e.g. a scratch insert-then-rollback) because: (a) it is on the order of a
/// single fast local write transaction — nowhere near PBKDF2's deliberately large, easily-distinguishable
/// cost — so it is a far weaker signal to begin with; (b) the "auth" rate-limit policy on this endpoint
/// caps an attacker at 5 requests/minute, which is nowhere near enough samples to reliably separate a
/// few-millisecond DB-write signal from ordinary network/DB jitter; and (c) building a real dummy-write
/// path purely for this would add genuine complexity/risk (a scratch table, or an explicit
/// begin-then-rollback transaction) for a marginal benefit against a channel that is already
/// impractical to exploit under (a)+(b). The response body's identical wording remains the primary,
/// load-bearing anti-enumeration guarantee; this residual is the same accepted, documented trade-off
/// Register already made for the analogous gap in its own flow.
/// </para>
/// </para>
/// <para>
/// <b>Token lifetime — 1 hour</b> (docs/TASKS.md's own P0-21 acceptance criterion: "token ใช้ครั้งเดียว
/// หมดอายุ 1 ชม."), deliberately much shorter than <see cref="UserSecurityTokenPurpose.EmailConfirmation"/>'s
/// 24 hours: a password-reset link is the higher-stakes of the two (successfully redeeming it lets
/// someone take over the account outright, not merely confirm an address they already control), so a
/// narrower window before it goes stale is the right trade-off even though it means a slower user has to
/// request a fresh one.
/// </para>
/// <para>
/// <b>Design decision — a new request invalidates any prior outstanding PasswordReset token for that
/// user</b> (task instruction: "make a specific, deliberate choice and document why"). Before issuing
/// the new token, this handler loads every currently-unconsumed <see cref="UserSecurityToken"/> row for
/// this user with Purpose = PasswordReset and, for whichever of those are not already expired, calls
/// <see cref="UserSecurityToken.Consume"/> on them too — reusing the exact same one-time-use mechanism
/// (never a second "invalidate" API) so that only the most recently issued reset link can ever succeed.
/// Chosen over letting every outstanding link stay independently valid because: a user who clicks
/// "forgot password" more than once almost always wants the newest email (older ones sitting in an
/// inbox/spam folder are more likely to be stale/forgotten-about than deliberately kept as backups), and
/// — more importantly from a security standpoint — an attacker who triggers extra reset requests for a
/// victim's address (the victim never opens any of them) must not leave multiple simultaneously-valid
/// tokens outstanding merely because the legitimate owner never got around to using or invalidating the
/// earlier ones themselves.
/// </para>
/// <para>
/// Only the exists-and-active branch writes anything: the invalidated old token row(s), a new
/// <see cref="UserSecurityToken"/> row, and a queued reset-link email — all added to the same
/// <see cref="AppDbContext"/> instance and committed in one <see cref="AppDbContext.SaveChangesAsync"/>
/// call (database.md: atomic multi-table writes). Deliberately does <b>not</b> touch the user's password
/// or any <see cref="UserSession"/>/<see cref="RefreshToken"/> — that is entirely <c>ResetPasswordHandler</c>'s
/// job, once the link is actually redeemed (task instruction: "do NOT reset the password or touch
/// sessions at this step").
/// </para>
/// </summary>
public sealed class ForgotPasswordHandler(
    AppDbContext dbContext,
    ISecurityTokenGenerator tokenGenerator,
    IEmailOutbox emailOutbox,
    IClock clock,
    IOptions<PasswordResetOptions> passwordResetOptions,
    ILogger<ForgotPasswordHandler> logger)
{
    /// <summary>1 hour, per docs/TASKS.md's P0-21 acceptance criterion — see this class's own doc
    /// comment for the full reasoning.</summary>
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);

    private const string SuccessMessage =
        "หากอีเมลนี้มีอยู่ในระบบและยืนยันอีเมลแล้ว เราได้ส่งลิงก์สำหรับตั้งรหัสผ่านใหม่ไปยังกล่องจดหมายของคุณแล้ว " +
        "กรุณาตรวจสอบอีเมล (รวมถึงโฟลเดอร์ Junk/Spam) ลิงก์นี้จะหมดอายุภายใน 1 ชั่วโมง";

    public async Task<Result<ForgotPasswordResponse>> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var normalizedEmail = command.Email.Trim().ToUpperInvariant();

        // AsNoTracking: unlike Login's/ConfirmEmail's equivalent lookup, this handler never mutates
        // `user` — the only writes below are the token/outbox rows and the outstanding-token
        // Consume() calls, none of which touch this entity (database.md: read-only queries must be
        // .AsNoTracking()).
        var user = await dbContext.Users()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.Status != UserStatus.Active)
        {
            // Anti-enumeration: identical response, no work done — see the class doc comment's Timing
            // parity section for why no dummy CPU-bound "burn" call belongs on this branch.
            return new ForgotPasswordResponse(SuccessMessage);
        }

        // Design decision (see class doc comment): invalidate any prior outstanding PasswordReset
        // token(s) for this user before issuing the new one, so only the most recently requested link
        // ever works. Only unexpired rows need an explicit Consume() — UserSecurityToken.Consume throws
        // on an already-expired token (by design, see its own doc comment), and an expired-but-still-
        // unconsumed row is already unusable on its own, so touching it here would add nothing.
        var outstandingTokens = await dbContext.UserSecurityTokens()
            .Where(t => t.UserId == user.Id && t.Purpose == UserSecurityTokenPurpose.PasswordReset && t.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var outstandingToken in outstandingTokens)
        {
            if (!outstandingToken.IsExpired(clock))
            {
                outstandingToken.Consume(clock);
            }
        }

        var (rawToken, tokenHash) = tokenGenerator.Generate();
        var resetToken = UserSecurityToken.Issue(
            user.Id,
            UserSecurityTokenPurpose.PasswordReset,
            tokenHash,
            clock.UtcNow.Add(ResetTokenLifetime));

        dbContext.UserSecurityTokens().Add(resetToken);

        var resetLink = BuildResetLink(rawToken);
        var bodyHtml = ForgotPasswordEmailContent.Render(user.DisplayName, resetLink);
        emailOutbox.Enqueue(user.Email, ForgotPasswordEmailContent.Subject, bodyHtml, ForgotPasswordEmailContent.TemplateKey);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("ForgotPassword: issued a password-reset token for user {UserId}.", user.Id);

        return new ForgotPasswordResponse(SuccessMessage);
    }

    private string BuildResetLink(string rawToken) =>
        $"{passwordResetOptions.Value.ResetPasswordUrl}?token={Uri.EscapeDataString(rawToken)}";
}
