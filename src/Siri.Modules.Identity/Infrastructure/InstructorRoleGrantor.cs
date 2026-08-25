using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>Implementation of <see cref="IInstructorRoleGrantor"/> — see that interface's own doc
/// comment for the full contract.</summary>
public sealed class InstructorRoleGrantor(AppDbContext dbContext) : IInstructorRoleGrantor
{
    public async Task GrantAsync(Guid userId, CancellationToken cancellationToken)
    {
        // .Include(Roles): without it, User.AssignRole's own idempotency check
        // (_roles.Any(r => r.Id == role.Id)) would run against an empty, never-loaded collection and
        // could never actually detect "already has this role" for a user whose roles were assigned
        // outside this call — silently correct today (the InstructorProfile state machine only ever
        // reaches Approve once per profile) but wrong to leave fragile in a method other callers may
        // depend on being genuinely idempotent later.
        var user = await dbContext.Users()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            throw new InvalidOperationException($"Cannot grant the Instructor role: no user {userId} exists.");
        }

        var instructorRole = await dbContext.Roles()
            .FirstOrDefaultAsync(r => r.Id == Role.InstructorId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorRole is null)
        {
            // Would mean the AddIdentityDomain migration (P0-14, which seeds the four fixed Role rows
            // via HasData) has not been applied to this database yet — same reasoning IdentitySeeder's
            // own matching guard gives.
            throw new InvalidOperationException(
                "The Instructor role was not found in identity.Roles. Has the AddIdentityDomain migration been applied?");
        }

        user.AssignRole(instructorRole);
    }
}
