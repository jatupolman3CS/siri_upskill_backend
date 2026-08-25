using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for the Identity module's entities on the shared
/// <see cref="AppDbContext"/>. <see cref="AppDbContext"/> itself deliberately carries no
/// <c>DbSet&lt;T&gt;</c> properties for module entities — it never references module projects (see
/// its own doc comment), and a real <c>DbSet&lt;User&gt;</c> property would require exactly that
/// reference, pointing straight back at this project (which already references
/// <c>Siri.Persistence</c>) and creating a circular project reference. Each module instead exposes
/// its own entities this way, over <see cref="DbContext.Set{TEntity}"/>, which needs no such reference.
/// </summary>
public static class AppDbContextIdentityExtensions
{
    public static DbSet<User> Users(this AppDbContext context) => context.Set<User>();

    public static DbSet<Role> Roles(this AppDbContext context) => context.Set<Role>();

    public static DbSet<UserSession> UserSessions(this AppDbContext context) => context.Set<UserSession>();

    public static DbSet<RefreshToken> RefreshTokens(this AppDbContext context) => context.Set<RefreshToken>();

    public static DbSet<SecurityAudit> SecurityAudits(this AppDbContext context) => context.Set<SecurityAudit>();

    public static DbSet<UserSecurityToken> UserSecurityTokens(this AppDbContext context) => context.Set<UserSecurityToken>();
}
