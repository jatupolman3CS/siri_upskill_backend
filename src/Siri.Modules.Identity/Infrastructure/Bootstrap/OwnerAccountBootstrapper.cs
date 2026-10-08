using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Infrastructure.Bootstrap;

/// <summary>An owner account that is now fully privileged — handed to the Catalog bootstrapper (by the
/// composition root) so it can attach an Approved instructor profile to the same user.</summary>
public sealed record OwnerAccount(Guid UserId, string DisplayName);

/// <summary>
/// Gives every account listed in <see cref="OwnerBootstrapOptions.OwnerEmails"/> all platform roles.
/// Runs from <c>Siri.Api/Program.cs</c> at startup (all environments, including Production — unlike the
/// dev-only <c>--seed</c> flag) using the application's own database role, so it works when nobody can
/// reach the database by hand.
/// <para>
/// <b>Safety rules</b> (this is privilege escalation driven by an email address, so it is deliberately
/// narrow):
/// <list type="bullet">
/// <item>Only an account that <em>already exists</em> and is <see cref="UserStatus.Active"/> is touched.
/// Active means the address was verified (confirmation link or Google), so a squatter who registered the
/// owner's email but never confirmed it (<see cref="UserStatus.PendingEmailConfirmation"/>) gets
/// nothing — the real owner's Google sign-in takes that account over first (see GoogleLogin's
/// pre-hijack defence). Suspended/Deleted accounts are skipped for the same reason.</item>
/// <item>It never creates an account and never touches a password.</item>
/// <item>Idempotent: roles already held are left alone, and a <c>SECURITY_AUDIT</c> row is written only
/// when something was actually granted, so a restart does not spam the audit log.</item>
/// <item>Emails are not logged (PII) — only their position in the configured list.</item>
/// </list>
/// The roles take effect at the owner's next sign-in (the access token carries the role claims).
/// </para>
/// </summary>
public sealed class OwnerAccountBootstrapper(
    AppDbContext dbContext,
    IClock clock,
    IOptions<OwnerBootstrapOptions> options,
    ILogger<OwnerAccountBootstrapper> logger)
{
    public const string AuditEventType = "OwnerBootstrapRolesGranted";

    /// <summary>Ensures each configured owner holds every role; returns the accounts that qualified
    /// (Active, now fully privileged), whether they were changed by this call or already complete.</summary>
    public async Task<IReadOnlyList<OwnerAccount>> EnsureAllRolesAsync(CancellationToken cancellationToken)
    {
        var configuredEmails = options.Value.OwnerEmails;
        var normalizedEmails = NormalizeEmails(configuredEmails);

        if (normalizedEmails.Count == 0)
        {
            return [];
        }

        var allRoles = await dbContext.Roles().ToListAsync(cancellationToken).ConfigureAwait(false);
        if (allRoles.Count == 0)
        {
            // Would mean the AddIdentityDomain migration (which seeds the fixed ROLE rows) has not been
            // applied to this database yet — a real environment problem, not something to work around.
            throw new InvalidOperationException(
                "No roles found in the 'IDENTITY.ROLES' table. Has the 'AddIdentityDomain' EF migration been applied to this database yet?");
        }

        var owners = new List<OwnerAccount>();
        var rolesGranted = false;

        for (var index = 0; index < normalizedEmails.Count; index++)
        {
            var normalizedEmail = normalizedEmails[index];

            var user = await dbContext.Users()
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                logger.LogWarning(
                    "Owner bootstrap: configured owner #{Index} has no account yet — sign in once, then restart the API.",
                    index);
                continue;
            }

            if (user.Status != UserStatus.Active)
            {
                logger.LogWarning(
                    "Owner bootstrap: configured owner #{Index} is {Status}, not Active — skipped (roles are only granted to verified, active accounts).",
                    index,
                    user.Status);
                continue;
            }

            var missingRoles = allRoles.Where(role => user.Roles.All(held => held.Id != role.Id)).ToList();
            foreach (var role in missingRoles)
            {
                user.AssignRole(role);
            }

            if (missingRoles.Count > 0)
            {
                var granted = string.Join(", ", missingRoles.Select(role => role.Name).Order());
                dbContext.SecurityAudits().Add(
                    SECURITY_AUDIT.Record(AuditEventType, user.Id, $"Granted by owner bootstrap: {granted}", null, clock));
                rolesGranted = true;

                logger.LogInformation(
                    "Owner bootstrap: granted {Count} role(s) to configured owner #{Index}: {Roles}.",
                    missingRoles.Count,
                    index,
                    granted);
            }

            owners.Add(new OwnerAccount(user.Id, user.DisplayName));
        }

        if (rolesGranted)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return owners;
    }

    /// <summary>Trims, drops blanks / values that are not email-shaped, upper-cases (the same
    /// normalization <c>USER.NormalizedEmail</c> uses) and removes duplicates, keeping configured order.</summary>
    public static IReadOnlyList<string> NormalizeEmails(IEnumerable<string?>? configured)
    {
        if (configured is null)
        {
            return [];
        }

        return configured
            .Select(email => email?.Trim())
            .Where(email => !string.IsNullOrEmpty(email) && email.Contains('@'))
            .Select(email => email!.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
