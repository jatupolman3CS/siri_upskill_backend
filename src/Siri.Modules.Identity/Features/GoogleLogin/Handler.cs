using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.GoogleLogin;

/// <summary>
/// Signs a user in with a Google ID token, creating or linking the local account as needed, and then
/// starts the exact same session <see cref="LoginHandler"/> does (via <see cref="LoginSessionIssuer"/>:
/// refresh-token cookie, SE-03 concurrent-session limit, Redis mirror, audit).
/// <para>
/// <b>Account resolution</b>, in order:
/// <list type="number">
/// <item>The Google <c>sub</c> is already linked → that user signs in (a suspended/deleted user is
/// refused with the generic error).</item>
/// <item>No link, but an account with the same email exists:
/// <list type="bullet">
/// <item><see cref="UserStatus.Active"/> (email already confirmed) → link Google to it. Google vouches
/// for ownership of that exact address (<c>email_verified</c> is required) so this is safe.</item>
/// <item><see cref="UserStatus.PendingEmailConfirmation"/> → <b>pre-hijack defence</b>: whoever
/// registered that address with a password never proved they own it, and may be an attacker waiting
/// for the real owner to show up. Google has now proved ownership, so the account is activated for the
/// Google user and its password is replaced with an unusable value — the squatter's password stops
/// working.</item>
/// <item>suspended/deleted → refused.</item>
/// </list></item>
/// <item>Otherwise a new, already-confirmed <see cref="UserStatus.Active"/> Learner account is created
/// with an unusable password (the user can still set one later through "forgot password").</item>
/// </list>
/// Every rejection that depends on the state of an existing account returns the same generic error as
/// a bad token, so this endpoint cannot be used to probe which emails are registered.
/// </para>
/// <para>
/// On every successful sign-in the ID token's <c>picture</c> claim fills <see cref="USER.AvatarUrl"/> if
/// (and only if) the account has no avatar yet — see <see cref="USER.SetAvatarIfMissing"/> for what is
/// accepted (https only, within the column width) and why an existing avatar is never overwritten.
/// </para>
/// </summary>
public sealed class GoogleLoginHandler(
    AppDbContext dbContext,
    IGoogleIdTokenVerifier tokenVerifier,
    LoginSessionIssuer sessionIssuer,
    IOptions<GoogleLoginOptions> options,
    IClock clock,
    ILogger<GoogleLoginHandler> logger)
{
    private const int MaxDisplayNameLength = 200; // matches Users.DisplayName's column width

    private static readonly DomainError NotConfiguredError =
        DomainError.Validation("การเข้าสู่ระบบด้วย Google ยังไม่เปิดใช้งาน");

    private static readonly DomainError SignInFailedError =
        DomainError.Validation("ไม่สามารถเข้าสู่ระบบด้วย Google ได้ กรุณาลองใหม่อีกครั้ง");

    public async Task<Result<LoginResult>> HandleAsync(
        GoogleLoginCommand command,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (!options.Value.IsEnabled)
        {
            return Result.Failure<LoginResult>(NotConfiguredError);
        }

        var identity = await tokenVerifier.VerifyAsync(command.IdToken, cancellationToken).ConfigureAwait(false);

        // An address Google has not verified proves nothing about who owns it, so it is never enough
        // to sign in to — or take over — an account.
        if (identity is null || !identity.EmailVerified)
        {
            return Result.Failure<LoginResult>(SignInFailedError);
        }

        try
        {
            return await SignInAsync(identity, command, userAgent, ipAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Two first-time sign-ins for the same person racing each other: the loser trips a unique
            // index (email or Google subject). Start over once with a clean change tracker — the
            // winner's rows are visible now, so the second pass takes the "already exists" branch.
            logger.LogInformation(ex, "Google sign-in lost a create/link race; retrying once.");
            dbContext.ChangeTracker.Clear();

            return await SignInAsync(identity, command, userAgent, ipAddress, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Result<LoginResult>> SignInAsync(
        GoogleIdentity identity,
        GoogleLoginCommand command,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var email = identity.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();

        var link = await dbContext.UserExternalLogins()
            .FirstOrDefaultAsync(
                l => l.Provider == ExternalLoginProvider.Google && l.ProviderSubject == identity.Subject,
                cancellationToken)
            .ConfigureAwait(false);

        USER? user;
        string auditDetail = """{"provider":"google"}""";

        if (link is not null)
        {
            user = await dbContext.Users()
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.Id == link.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (user is null || user.Status != UserStatus.Active)
            {
                return Result.Failure<LoginResult>(SignInFailedError);
            }

            link.RecordLogin(clock);
        }
        else
        {
            user = await dbContext.Users()
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                user = await CreateUserAsync(identity, email, normalizedEmail, cancellationToken).ConfigureAwait(false);
                auditDetail = """{"provider":"google","accountCreated":true}""";
            }
            else if (user.Status == UserStatus.PendingEmailConfirmation)
            {
                // Pre-hijack defence — see the class doc comment. Order matters: the password is
                // replaced before the account becomes Active so there is never an Active account that
                // still accepts the squatter's password.
                user.ChangePassword(UnusablePasswordHash());
                user.ConfirmEmail(clock);
                auditDetail = """{"provider":"google","pendingAccountActivated":true}""";
            }
            else if (user.Status != UserStatus.Active)
            {
                return Result.Failure<LoginResult>(SignInFailedError);
            }

            dbContext.UserExternalLogins().Add(
                USER_EXTERNAL_LOGIN.Link(user.Id, ExternalLoginProvider.Google, identity.Subject, email, clock));
        }

        // Fill the avatar from Google's `picture` claim only when the account has none yet — the domain
        // method refuses non-https / over-long / unusable values and never overwrites an existing
        // avatar. Reached by every successful branch above (new, linked, activated-pending) and tracked
        // by the context, so the save below persists it with everything else.
        user.SetAvatarIfMissing(identity.Picture);

        // Commits whatever was staged above (new user / link / activation) together with the session.
        return await sessionIssuer.IssueAsync(
            user,
            command.DeviceId,
            command.DeviceName,
            userAgent,
            ipAddress,
            "login.succeeded",
            auditDetail,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<USER> CreateUserAsync(
        GoogleIdentity identity, string email, string normalizedEmail, CancellationToken cancellationToken)
    {
        var user = USER.Register(email, normalizedEmail, UnusablePasswordHash(), ResolveDisplayName(identity, email));
        user.ConfirmEmail(clock); // Google already verified the address.

        var learnerRole = await dbContext.Roles()
            .FirstAsync(r => r.Id == ROLE.LearnerId, cancellationToken)
            .ConfigureAwait(false);
        user.AssignRole(learnerRole);

        dbContext.Users().Add(user);

        return user;
    }

    private static string ResolveDisplayName(GoogleIdentity identity, string email)
    {
        var name = string.IsNullOrWhiteSpace(identity.Name) ? email.Split('@')[0] : identity.Name.Trim();

        return name.Length > MaxDisplayNameLength ? name[..MaxDisplayNameLength] : name;
    }

    /// <summary>A value that can never verify as a password hash (not a valid ASP.NET Core Identity
    /// hash, so <c>VerifyPassword</c> always answers "failed") — same approach as account anonymisation.</summary>
    private static string UnusablePasswordHash() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
