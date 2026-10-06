using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Login;

/// <summary>
/// Verifies an email/password pair and, on success, starts a new <see cref="USER_SESSION"/> +
/// <see cref="REFRESH_TOKEN"/> + access token for it.
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
/// On success: <see cref="USER.RecordLogin"/>, a new <see cref="USER_SESSION"/>
/// (<see cref="USER_SESSION.Start"/>) capturing device/UA/IP, a new <see cref="REFRESH_TOKEN"/>
/// (<see cref="REFRESH_TOKEN.Issue"/>) linked to that session via its raw-token-hash pair from
/// <see cref="ISecurityTokenGenerator"/> (the exact same "generate random, hash before storing"
/// pattern P0-15 already established for <see cref="USER_SECURITY_TOKEN"/> — reused here rather than
/// inventing a second one, per docs/DATABASE.md's "RefreshTokens.TokenHash" being documented the same
/// way), a new access token (<see cref="IAccessTokenGenerator"/>), and a <see cref="SECURITY_AUDIT"/>
/// entry — all added to the same <see cref="AppDbContext"/> and committed in one
/// <see cref="AppDbContext.SaveChangesAsync"/> call (database.md: atomic multi-table writes).
/// </para>
/// <para>
/// <b>SE-03 concurrent-session enforcement</b> (P0-17, security.md §2/ARCHITECTURE.md §5) also runs
/// here, after the new session/refresh-token/audit rows above are staged but before that same
/// <see cref="AppDbContext.SaveChangesAsync"/> call: <c>LoginSessionIssuer.IssueAsync</c>
/// loads every other currently-active <see cref="USER_SESSION"/> for this user, combines them with the
/// one just created, and — via the pure <see cref="ConcurrentSessionEvictionPolicy"/> — decides which
/// (if any) must be evicted to stay within the effective limit
/// (<see cref="USER.MaxConcurrentSessionsOverride"/> if set, else
/// <see cref="ConcurrentSessionOptions.MaxConcurrentSessions"/>). Each eviction revokes the session +
/// its still-active refresh token(s), writes a distinct <see cref="SECURITY_AUDIT"/> row, and queues a
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
    LoginSessionIssuer sessionIssuer)
{
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

        // Session, refresh token, access token, SE-03 eviction, audit and Redis mirror all live in
        // LoginSessionIssuer so every sign-in method (password here, Google elsewhere) shares them.
        return await sessionIssuer.IssueAsync(
            user,
            command.DeviceId,
            command.DeviceName,
            userAgent,
            ipAddress,
            "login.succeeded",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    private void VerifyAgainstDummyHash(string password)
    {
        var dummyUser = USER.Register("dummy@example.invalid", "DUMMY@EXAMPLE.INVALID", "placeholder", "placeholder");
        passwordHasher.VerifyPassword(dummyUser, DummyPasswordHash, password);
    }
}
