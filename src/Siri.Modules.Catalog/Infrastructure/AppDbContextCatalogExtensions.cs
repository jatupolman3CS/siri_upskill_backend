using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for the Catalog module's entities on the shared
/// <see cref="AppDbContext"/> — same reasoning as <c>Siri.Modules.Identity.Infrastructure
/// .AppDbContextIdentityExtensions</c>'s own doc comment (<see cref="AppDbContext"/> carries no
/// module-owned <c>DbSet&lt;T&gt;</c> properties to avoid a circular project reference).
/// <para>
/// Course's child tables (<see cref="CourseSections"/>/<see cref="CourseEpisodes"/>/
/// <see cref="CourseOutcomes"/>/<see cref="CourseRequirements"/>) get their own accessors too, despite
/// <see cref="Course"/> being their aggregate root — database.md prefers projecting straight to DTOs
/// over materializing whole graphs, and later tasks (P1-06/P1-07 read models, P4-01 batch reorder) will
/// want direct/bulk access without loading full <see cref="Course"/> graphs every time.
/// </para>
/// </summary>
public static class AppDbContextCatalogExtensions
{
    public static DbSet<Category> Categories(this AppDbContext context) => context.Set<Category>();

    public static DbSet<Course> Courses(this AppDbContext context) => context.Set<Course>();

    public static DbSet<CourseSection> CourseSections(this AppDbContext context) => context.Set<CourseSection>();

    public static DbSet<CourseEpisode> CourseEpisodes(this AppDbContext context) => context.Set<CourseEpisode>();

    public static DbSet<CourseOutcome> CourseOutcomes(this AppDbContext context) => context.Set<CourseOutcome>();

    public static DbSet<CourseRequirement> CourseRequirements(this AppDbContext context) => context.Set<CourseRequirement>();

    public static DbSet<InstructorProfile> InstructorProfiles(this AppDbContext context) => context.Set<InstructorProfile>();

    public static DbSet<EpisodeAttachment> EpisodeAttachments(this AppDbContext context) => context.Set<EpisodeAttachment>();

    public static DbSet<LearningPath> LearningPaths(this AppDbContext context) => context.Set<LearningPath>();

    public static DbSet<LearningPathItem> LearningPathItems(this AppDbContext context) => context.Set<LearningPathItem>();
}
