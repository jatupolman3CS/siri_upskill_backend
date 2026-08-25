using Microsoft.EntityFrameworkCore;
using Siri.Modules.Community.Domain;
using Siri.Persistence;

namespace Siri.Modules.Community.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for the Community module's entities on the shared
/// <see cref="AppDbContext"/> — mirrors the existing zero-<c>DbSet</c>-on-<c>AppDbContext</c> pattern
/// (same reasoning as <c>Siri.Modules.Catalog.Infrastructure.AppDbContextCatalogExtensions</c>'s own doc
/// comment: <see cref="AppDbContext"/> carries no module-owned <c>DbSet&lt;T&gt;</c> properties, to avoid
/// a circular project reference back to every module).
/// <para>
/// The entity type names are UPPERCASE (D-17), but these accessor method names are not — same
/// "Repository/Service/DTO/interface/namespace names are NOT uppercased" rule this scaffold applies to
/// <see cref="Application.IDiscussionRepository"/>/<see cref="Infrastructure.DiscussionRepository"/> etc.,
/// extended to this style of accessor too.
/// </para>
/// </summary>
public static class AppDbContextCommunityExtensions
{
    public static DbSet<DISCUSSION> Discussions(this AppDbContext context) => context.Set<DISCUSSION>();

    public static DbSet<REPORT> Reports(this AppDbContext context) => context.Set<REPORT>();
}
