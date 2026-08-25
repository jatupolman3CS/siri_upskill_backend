using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Community.Domain;

namespace Siri.Modules.Community.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="DISCUSSION"/> — see docs/DATABASE.md's "community" section and
/// <see cref="DISCUSSION"/>'s own doc comment for the full D-17 UPPERCASE-naming reasoning.
/// <para>
/// Every UPPERCASE C# property below maps to an identically-named column by EF's default convention — no
/// <c>.HasColumnName(...)</c> needed for those (this project has no snake_case/naming-convention package
/// installed; confirmed by grepping backend/Directory.Packages.props + every module's <c>.csproj</c> for
/// one). Only the six <see cref="Siri.Persistence.Conventions.IAuditable"/>/
/// <see cref="Siri.Persistence.Conventions.ISoftDelete"/> properties (kept PascalCase in C# — see
/// <see cref="DISCUSSION"/>'s own doc comment for why) get an explicit <c>.HasColumnName(...)</c> override
/// so their *columns* still land UPPERCASE like every other column on this table.
/// </para>
/// </summary>
public sealed class DiscussionConfiguration : IEntityTypeConfiguration<DISCUSSION>
{
    public void Configure(EntityTypeBuilder<DISCUSSION> builder)
    {
        builder.ToTable("DISCUSSIONS", "community");

        builder.HasKey(d => d.DISCUSSION_ID);

        builder.Property(d => d.COURSE_ID).IsRequired();
        builder.Property(d => d.EPISODE_ID);
        builder.Property(d => d.USER_ID).IsRequired();
        builder.Property(d => d.PARENT_ID);

        builder.Property(d => d.BODY).HasMaxLength(4000).IsRequired();
        builder.Property(d => d.IS_INSTRUCTOR_ANSWER).IsRequired();

        builder.Property(d => d.STATUS).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(d => d.UPVOTE_COUNT).IsRequired();

        // Self-referencing FK for threaded replies. Restrict, not Cascade: same reasoning already
        // documented on Siri.Modules.Catalog.Domain.Category's self-referencing ParentId — SQL Server
        // rejects ON DELETE CASCADE on a self-referencing FK outright ("may cause cycles or multiple
        // cascade paths"). Bare .WithMany() — DISCUSSION deliberately has no Replies navigation
        // collection (same "flat query, assemble in memory if ever needed" reasoning as Category, not an
        // oversight) — but .HasForeignKey() is still explicit per this scaffold's rules: never rely on
        // convention discovery (this codebase already hit a real shadow-FK bug once, on Course.Sections).
        builder.HasOne<DISCUSSION>()
            .WithMany()
            .HasForeignKey(d => d.PARENT_ID)
            .OnDelete(DeleteBehavior.Restrict);

        // No FK constraint to catalog.Courses/catalog.CourseEpisodes (COURSE_ID/EPISODE_ID) or
        // identity.Users (USER_ID) — cross-module/cross-schema, same reasoning already documented on
        // Siri.Modules.Catalog.Domain.Course.TrailerMediaAssetId (docs/ARCHITECTURE.md §1: a
        // database-level FK spanning two modules' schemas is exactly the physical coupling keeping
        // modules as separate projects avoids).

        // Matches docs/DATABASE.md's community section exactly: "IX(EpisodeId, Status, CreatedAtUtc)" —
        // the classroom Q&A tab's primary query (IDiscussionRepository.ListByEpisodeAsync).
        builder.HasIndex(d => new { d.EPISODE_ID, d.STATUS, d.CreatedAtUtc });

        // ---- IAuditable / ISoftDelete: UPPERCASE column, PascalCase C# property (see DISCUSSION's own
        // doc comment for why) ----------------------------------------------------------------------
        builder.Property(d => d.IsDeleted).HasColumnName("IS_DELETED").IsRequired();
        builder.Property(d => d.DeletedAtUtc).HasColumnName("DELETED_AT_UTC").HasPrecision(3);

        builder.Property(d => d.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(d => d.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(d => d.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(d => d.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
