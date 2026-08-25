using Microsoft.EntityFrameworkCore;
using Siri.Modules.Media.Domain;
using Siri.Persistence;

namespace Siri.Modules.Media.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for the Media module's entities on the shared <see cref="AppDbContext"/> —
/// same reasoning as <c>Siri.Modules.Catalog.Infrastructure.AppDbContextCatalogExtensions</c>'s own doc
/// comment (<see cref="AppDbContext"/> carries no module-owned <c>DbSet&lt;T&gt;</c> properties, to avoid a
/// circular project reference — entities are discovered purely via <c>IEntityTypeConfiguration&lt;T&gt;</c>
/// assembly scanning). Method names stay PascalCase like every other module's equivalent extension methods
/// — only entity classes/properties that map 1:1 to a table/column follow the D-17 UPPERCASE exception,
/// not helper method names (see <see cref="MEDIA_ASSET"/>'s own doc comment).
/// </summary>
public static class AppDbContextMediaExtensions
{
    public static DbSet<MEDIA_ASSET> MediaAssets(this AppDbContext context) => context.Set<MEDIA_ASSET>();

    public static DbSet<MEDIA_UPLOAD_SESSION> MediaUploadSessions(this AppDbContext context) => context.Set<MEDIA_UPLOAD_SESSION>();

    public static DbSet<PLAYBACK_SESSION> PlaybackSessions(this AppDbContext context) => context.Set<PLAYBACK_SESSION>();
}
