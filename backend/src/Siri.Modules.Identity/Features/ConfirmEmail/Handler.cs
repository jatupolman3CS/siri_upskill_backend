using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.ConfirmEmail;

/// <summary>
/// Redeems a raw email-confirmation token: hashes it the same way <c>Register.Handler</c> hashed it
/// at issuance, looks the row up by that hash, and — only if it is found, unexpired, unconsumed, and
/// still points at a user genuinely waiting on confirmation — flips the user to
/// <see cref="UserStatus.Active"/> and consumes the token.
/// <para>
/// <b>Anti-enumeration parallel to Register</b> (security.md's spirit, and this task's other main
/// security-review point): a "wrong token" response that reads differently from an "expired token"
/// or "already used" response lets an attacker narrow down which reason applies, effectively
/// confirming a guess is real even without ever landing a successful confirmation. So every rejection
/// path below — not found, expired, already consumed, or (defensively) a user no longer in
/// <see cref="UserStatus.PendingEmailConfirmation"/> — returns the exact same
/// <see cref="InvalidTokenError"/>, never a distinguishable one.
/// </para>
/// </summary>
public sealed class ConfirmEmailHandler(
    AppDbContext dbContext,
    ISecurityTokenGenerator tokenGenerator,
    IClock clock)
{
    private static readonly DomainError InvalidTokenError =
        DomainError.Validation("ลิงก์ยืนยันอีเมลไม่ถูกต้องหรือหมดอายุแล้ว กรุณาสมัครสมาชิกใหม่อีกครั้งหากยังไม่ได้ยืนยันบัญชี");

    private const string SuccessMessage = "อีเมลของคุณได้รับการยืนยันเรียบร้อยแล้ว คุณสามารถเข้าสู่ระบบได้ทันที";

    public async Task<Result<ConfirmEmailResponse>> HandleAsync(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        var tokenHash = tokenGenerator.Hash(command.Token);

        var securityToken = await dbContext.UserSecurityTokens()
            .FirstOrDefaultAsync(
                t => t.TokenHash == tokenHash && t.Purpose == UserSecurityTokenPurpose.EmailConfirmation,
                cancellationToken)
            .ConfigureAwait(false);

        if (securityToken is null || !securityToken.IsValid(clock))
        {
            return Result.Failure<ConfirmEmailResponse>(InvalidTokenError);
        }

        var user = await dbContext.Users()
            .FirstOrDefaultAsync(u => u.Id == securityToken.UserId, cancellationToken)
            .ConfigureAwait(false);

        // Neither branch below should be reachable in practice — the FK from UserSecurityTokens to
        // Users is Restrict (a user row can't disappear while a token references it), and a token is
        // only ever issued for a user that is currently PendingEmailConfirmation. Checked anyway
        // (never trust a lookup to "obviously" succeed) and, per this handler's whole point, mapped
        // to the exact same generic error as every other rejection reason, not a different one.
        if (user is null || user.Status != UserStatus.PendingEmailConfirmation)
        {
            return Result.Failure<ConfirmEmailResponse>(InvalidTokenError);
        }

        user.ConfirmEmail(clock);
        securityToken.Consume(clock);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ConfirmEmailResponse(SuccessMessage);
    }
}
