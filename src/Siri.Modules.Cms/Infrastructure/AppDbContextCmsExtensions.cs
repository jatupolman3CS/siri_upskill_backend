using Microsoft.EntityFrameworkCore;
using Siri.Modules.Cms.Domain;
using Siri.Persistence;

namespace Siri.Modules.Cms.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for the Cms module's entities on the shared <see cref="AppDbContext"/> —
/// same reasoning as <c>Siri.Modules.Catalog.Infrastructure.AppDbContextCatalogExtensions</c>'s own doc
/// comment (<see cref="AppDbContext"/> carries no module-owned <c>DbSet&lt;T&gt;</c> properties, to avoid a
/// circular project reference back from <c>Siri.Persistence</c>). Method names stay ordinary PascalCase —
/// docs/DECISIONS.md D-17's UPPERCASE rule targets entity class/property names and DB tables/columns only,
/// not accessor method names like these (.claude/rules/backend.md's own "Repository/Service/DTO/interface
/// names are NOT uppercased" carve-out applies the same way here).
/// <para>
/// Only the <c>{Entity}Repository</c> classes in this namespace call these — <c>Application/</c> talks to
/// the <c>I{Entity}Repository</c> abstractions instead, never this class or <see cref="AppDbContext"/>
/// directly (docs/DECISIONS.md D-17's Repository+Service pattern).
/// </para>
/// </summary>
public static class AppDbContextCmsExtensions
{
    public static DbSet<BANNER> Banners(this AppDbContext context) => context.Set<BANNER>();

    public static DbSet<MENU_ITEM> MenuItems(this AppDbContext context) => context.Set<MENU_ITEM>();

    public static DbSet<POST> Posts(this AppDbContext context) => context.Set<POST>();

    public static DbSet<REDIRECT> Redirects(this AppDbContext context) => context.Set<REDIRECT>();
}
