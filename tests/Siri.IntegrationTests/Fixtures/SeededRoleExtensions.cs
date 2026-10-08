using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;

namespace Siri.IntegrationTests.Fixtures;

/// <summary>
/// The four system roles are migration-seeded reference data (<c>RoleConfiguration.HasData</c>). A test that
/// assigns one to a user must hand EF the row that already exists — <c>new ROLE(ROLE.AdminId, …)</c> looks like a
/// new entity to the change tracker, so <c>Users.Add(user)</c> tries to INSERT it again (PK_ROLES violation) and a
/// second user in the same context hits "another instance with the same key is already being tracked".
/// <c>FindAsync</c> returns the already-tracked instance when there is one and loads the seeded row otherwise.
/// </summary>
public static class SeededRoleExtensions
{
    public static async Task<ROLE> SeededRoleAsync(this AppDbContext dbContext, Guid roleId) =>
        await dbContext.Roles().FindAsync(roleId)
        ?? throw new InvalidOperationException($"Seeded role {roleId} is missing — was the migration applied?");
}
