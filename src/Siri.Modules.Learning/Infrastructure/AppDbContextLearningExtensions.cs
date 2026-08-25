using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for this module's Enrollment/Progress/Certificate cluster entities on the
/// shared <see cref="AppDbContext"/> — same reasoning as <c>Siri.Modules.Catalog.Infrastructure
/// .AppDbContextCatalogExtensions</c>'s own doc comment (<see cref="AppDbContext"/> carries no
/// module-owned <c>DbSet&lt;T&gt;</c> properties to avoid a circular project reference).
/// <para>
/// A SEPARATE file from <c>AppDbContextLearningQuizExtensions.cs</c> (the Quiz/Assignment cluster,
/// scaffolded in parallel by a sibling task/agent) — see that file's own doc comment for why the split
/// exists (avoiding two agents editing the same file concurrently) and why a later cleanup pass may merge
/// them once both parts have landed. Neither file references the other.
/// </para>
/// </summary>
public static class AppDbContextLearningExtensions
{
    public static DbSet<ENROLLMENT> Enrollments(this AppDbContext context) => context.Set<ENROLLMENT>();

    public static DbSet<EPISODE_PROGRESS> EpisodeProgresses(this AppDbContext context) => context.Set<EPISODE_PROGRESS>();

    public static DbSet<WATCH_EVENT> WatchEvents(this AppDbContext context) => context.Set<WATCH_EVENT>();

    public static DbSet<CERTIFICATE> Certificates(this AppDbContext context) => context.Set<CERTIFICATE>();
}
