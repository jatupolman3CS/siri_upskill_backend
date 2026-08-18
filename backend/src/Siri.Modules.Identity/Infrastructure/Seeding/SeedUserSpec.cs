namespace Siri.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// One row of dev/test bootstrap data (task P0-37) — an account <see cref="IdentitySeeder"/> creates
/// (if it doesn't already exist by normalized email) with the given role and password. Deliberately
/// a plain, behavior-free record so the data set itself (<see cref="IdentitySeedData.BuildUsers"/>)
/// stays pure and unit-testable, independent of <c>AppDbContext</c>/<c>IUserPasswordHasher</c>/
/// <c>IClock</c>.
/// </summary>
public sealed record SeedUserSpec(string Email, string DisplayName, Guid RoleId, string Password);
