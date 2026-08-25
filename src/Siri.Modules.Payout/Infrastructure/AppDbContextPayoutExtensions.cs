using Microsoft.EntityFrameworkCore;
using Siri.Modules.Payout.Domain;
using Siri.Persistence;

namespace Siri.Modules.Payout.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for the Payout module's entities on the shared <see cref="AppDbContext"/>
/// — same reasoning as Catalog's <c>Infrastructure.AppDbContextCatalogExtensions</c>'s own doc comment
/// (<see cref="AppDbContext"/> carries no module-owned <c>DbSet&lt;T&gt;</c> properties, to avoid a
/// circular project reference since <c>Siri.Persistence</c> never references module projects).
/// <para>
/// <see cref="PAYOUT_BATCH_ITEM"/> gets its own accessor too, despite <see cref="PAYOUT_BATCH"/> being its
/// aggregate root — same reasoning Catalog gives for <c>CourseSections()</c>/<c>CourseEpisodes()</c>
/// alongside <c>Courses()</c>: database.md prefers projecting straight to DTOs over materializing whole
/// graphs, and the repository layer here (<see cref="PayoutBatchItemRepository"/>) needs direct access
/// without loading full <see cref="PAYOUT_BATCH"/> graphs every time.
/// </para>
/// </summary>
public static class AppDbContextPayoutExtensions
{
    public static DbSet<REVENUE_SPLIT> RevenueSplits(this AppDbContext context) => context.Set<REVENUE_SPLIT>();

    public static DbSet<INSTRUCTOR_PAYOUT_ACCOUNT> InstructorPayoutAccounts(this AppDbContext context) =>
        context.Set<INSTRUCTOR_PAYOUT_ACCOUNT>();

    public static DbSet<PAYOUT_BATCH> PayoutBatches(this AppDbContext context) => context.Set<PAYOUT_BATCH>();

    public static DbSet<PAYOUT_BATCH_ITEM> PayoutBatchItems(this AppDbContext context) => context.Set<PAYOUT_BATCH_ITEM>();
}
