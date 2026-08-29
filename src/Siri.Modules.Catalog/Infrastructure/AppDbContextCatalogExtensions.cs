using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for the Catalog module's entities on the shared
/// <see cref="AppDbContext"/> — same reasoning as <c>Siri.Modules.Identity.Infrastructure
/// .AppDbContextIdentityExtensions</c>'s own doc comment (<see cref="AppDbContext"/> carries no
/// module-owned <c>DbSet&lt;T&gt;</c> properties to avoid a circular project reference).
/// </summary>
public static class AppDbContextCatalogExtensions
{
    public static DbSet<CATEGORY> Categories(this AppDbContext context) => context.Set<CATEGORY>();

    public static DbSet<COURSE> Courses(this AppDbContext context) => context.Set<COURSE>();

    public static DbSet<COURSE_SECTION> CourseSections(this AppDbContext context) => context.Set<COURSE_SECTION>();

    public static DbSet<COURSE_EPISODE> CourseEpisodes(this AppDbContext context) => context.Set<COURSE_EPISODE>();

    public static DbSet<COURSE_OUTCOME> CourseOutcomes(this AppDbContext context) => context.Set<COURSE_OUTCOME>();

    public static DbSet<COURSE_REQUIREMENT> CourseRequirements(this AppDbContext context) => context.Set<COURSE_REQUIREMENT>();

    public static DbSet<INSTRUCTOR_PROFILE> InstructorProfiles(this AppDbContext context) => context.Set<INSTRUCTOR_PROFILE>();

    public static DbSet<EPISODE_ATTACHMENT> EpisodeAttachments(this AppDbContext context) => context.Set<EPISODE_ATTACHMENT>();

    public static DbSet<LEARNING_PATH> LearningPaths(this AppDbContext context) => context.Set<LEARNING_PATH>();

    public static DbSet<LEARNING_PATH_ITEM> LearningPathItems(this AppDbContext context) => context.Set<LEARNING_PATH_ITEM>();

    public static DbSet<COURSE_REVIEW> CourseReviews(this AppDbContext context) => context.Set<COURSE_REVIEW>();

    public static DbSet<WISHLIST_ITEM> Wishlists(this AppDbContext context) => context.Set<WISHLIST_ITEM>();
}
