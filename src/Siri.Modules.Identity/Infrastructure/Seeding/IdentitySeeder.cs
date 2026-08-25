using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// Task P0-37: creates the fixed dev/test bootstrap accounts (<see cref="IdentitySeedData.BuildUsers"/>)
/// against whatever database <see cref="AppDbContext"/> is currently configured for — a future local
/// Docker instance or the current remote one — never assuming a specific connection target. Invoked
/// from <c>Siri.Api/Program.cs</c>'s <c>--seed</c> CLI flag; see that file for exactly how.
/// <para>
/// <b>Task P1-30 addition:</b> <see cref="SeedAsync"/> returns every seeded account's id keyed by its
/// (trimmed, original-case) email — both freshly-created rows and rows that already existed and were
/// skipped, so the returned map is always complete regardless of fresh-run vs re-run. <c>Program.cs</c>
/// hands this to <c>CatalogSeeder</c> so it can attach the 5 seeded Instructor accounts' real user ids
/// to the <c>InstructorProfile</c> rows it creates, instead of inventing disconnected ids nothing can
/// ever log in as. Catalog cannot look these ids up itself — it may not reference this module's
/// <c>Domain</c>/<c>Infrastructure</c> namespaces (architecture.md module boundary) — so returning them
/// from here and threading them through the composition root (<c>Program.cs</c>) is the only option that
/// respects that boundary.
/// </para>
/// <para>
/// <b>Idempotent, and safe next to unrelated real data</b> (task requirement): for each spec, this
/// checks for an existing row by its exact known email (<see cref="AppDbContext.Users"/>
/// filtered to that one <c>NormalizedEmail</c>) and skips it if found — it never enumerates or
/// touches any other row in <c>Users</c>. Running this twice against the same database creates zero
/// new rows the second time; running it against a database that already has other, real, unrelated
/// users never looks at those rows at all. The four Roles this data set assigns
/// (<see cref="Role.AdminId"/>/<see cref="Role.LearnerId"/>/<see cref="Role.InstructorId"/>) are
/// never created here — they already exist as schema-level seed data from the
/// <c>AddIdentityDomain</c> migration's <c>RoleConfiguration.HasData</c> (task P0-14); this only
/// looks them up and reads their tracked instances so <see cref="User.AssignRole"/> can wire the
/// existing join-table FK, never inserting a competing <c>Role</c> row with the same id.
/// </para>
/// <para>
/// <b>Deliberately bypasses the real registration flow</b> (task instruction, explicit exception to
/// "always go through the real flow" — seed/test data only, never for anything real): builds each
/// <see cref="User"/> via the exact same <see cref="User.Register"/> factory
/// <c>Features/Register/Handler.cs</c> uses, hashes the password through the same
/// <see cref="IUserPasswordHasher"/> every real registration/login path uses (no second hashing
/// scheme), then calls <see cref="User.ConfirmEmail"/> immediately instead of issuing/redeeming a
/// <c>UserSecurityToken</c> confirmation link — landing every seeded account in
/// <see cref="UserStatus.Active"/> with <c>EmailConfirmedAtUtc</c> set, exactly as the task requires,
/// without sending any real email.
/// </para>
/// </summary>
public sealed class IdentitySeeder(
    AppDbContext dbContext,
    IUserPasswordHasher passwordHasher,
    IClock clock,
    IOptions<SeedOptions> seedOptions,
    ILogger<IdentitySeeder> logger)
{
    public async Task<IReadOnlyDictionary<string, Guid>> SeedAsync(CancellationToken cancellationToken)
    {
        var options = seedOptions.Value;
        SeedOptionsGuard.EnsureRealPasswordsConfigured(options);

        var specs = IdentitySeedData.BuildUsers(options);

        var roleIds = specs.Select(s => s.RoleId).Distinct().ToArray();
        var roles = await dbContext.Roles()
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken)
            .ConfigureAwait(false);

        var createdCount = 0;
        var skippedCount = 0;
        var userIdsByEmail = new Dictionary<string, Guid>();

        foreach (var spec in specs)
        {
            var email = spec.Email.Trim();
            var normalizedEmail = email.ToUpperInvariant(); // same normalization User.NormalizedEmail/RegisterHandler use

            var existingId = await dbContext.Users()
                .AsNoTracking()
                .Where(u => u.NormalizedEmail == normalizedEmail)
                .Select(u => (Guid?)u.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (existingId is { } id)
            {
                logger.LogInformation("Seed: {Email} already exists — skipping (idempotent).", email);
                userIdsByEmail[email] = id;
                skippedCount++;
                continue;
            }

            if (!roles.TryGetValue(spec.RoleId, out var role))
            {
                // Would mean the AddIdentityDomain migration (P0-14, which seeds the four fixed
                // Role rows via HasData) has not been applied to this database yet — a real
                // environment problem, not something to silently work around.
                throw new InvalidOperationException(
                    $"Seed role {spec.RoleId} was not found in the 'identity.Roles' table. " +
                    "Has the 'AddIdentityDomain' EF migration been applied to this database yet?");
            }

            var user = User.Register(email, normalizedEmail, HashPassword(email, normalizedEmail, spec.Password), spec.DisplayName);

            // Deliberate exception to "always go through the real flow" (task instruction) — see
            // this class's own doc comment above.
            user.ConfirmEmail(clock);
            user.AssignRole(role);

            dbContext.Users().Add(user);
            userIdsByEmail[email] = user.Id;
            createdCount++;
        }

        if (createdCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Seed: {CreatedCount} account(s) created, {SkippedCount} already existed (of {TotalCount} total seed accounts).",
            createdCount,
            skippedCount,
            specs.Count);

        return userIdsByEmail;
    }

    /// <summary>Same throwaway-instance-then-hash pattern <c>Features/Register/Handler.cs</c>'s own
    /// <c>HashPassword</c> uses — <see cref="IUserPasswordHasher.HashPassword"/> needs a
    /// <see cref="User"/> instance to call but never reads any of its properties.</summary>
    private string HashPassword(string email, string normalizedEmail, string password)
    {
        var throwawayUser = User.Register(email, normalizedEmail, "placeholder", "placeholder");
        return passwordHasher.HashPassword(throwawayUser, password);
    }
}
