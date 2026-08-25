using Microsoft.EntityFrameworkCore;
using Siri.Modules.Analytics.Domain;
using Siri.Persistence;

namespace Siri.Modules.Analytics.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for the Analytics module's entities on the shared <see cref="AppDbContext"/>
/// — same reasoning as <c>Siri.Modules.Catalog.Infrastructure.AppDbContextCatalogExtensions</c>'s own doc
/// comment (<see cref="AppDbContext"/> carries no module-owned <c>DbSet&lt;T&gt;</c> properties, to avoid a
/// circular project reference — each module adds its own via an extension method like this instead).
/// </summary>
public static class AppDbContextAnalyticsExtensions
{
    public static DbSet<DAILY_COURSE_STAT> DailyCourseStats(this AppDbContext context) => context.Set<DAILY_COURSE_STAT>();

    public static DbSet<EPISODE_DROP_OFF> EpisodeDropOffs(this AppDbContext context) => context.Set<EPISODE_DROP_OFF>();
}
