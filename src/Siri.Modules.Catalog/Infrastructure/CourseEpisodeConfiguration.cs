using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="COURSE_EPISODE"/>.
/// <para>
/// <see cref="COURSE_EPISODE.CourseId"/> and <see cref="COURSE_EPISODE.SectionId"/> both ultimately root
/// at <see cref="COURSE"/> — <c>SectionId → CourseSections.Id</c> is the real ownership edge
/// (<c>Cascade</c>), but the denormalized <c>CourseId → Courses.Id</c> FK <em>must</em> be
/// <c>Restrict</c>/<c>NoAction</c>, not <c>Cascade</c>: SQL Server rejects a schema where two different
/// <c>Cascade</c> paths both reach the same descendant table ("may cause cycles or multiple cascade
/// paths") — here, <c>CourseEpisodes</c> is reachable both via <c>Courses → CourseSections → CourseEpisodes</c>
/// and directly via <c>Courses → CourseEpisodes</c>. <c>NoAction</c> on the direct edge does not block
/// cascading — episode rows still get removed through the real Section→Episode chain within the same
/// cascading delete statement.
/// </para>
/// <see cref="COURSE_EPISODE.MediaAssetId"/> has no FK constraint at all — see
/// <see cref="COURSE_EPISODE"/>'s own doc comment and <c>CourseConfiguration</c>'s (same reasoning as
/// <c>TrailerMediaAssetId</c>: cross-module, never gets a DB-level FK).
/// </summary>
public sealed class CourseEpisodeConfiguration : IEntityTypeConfiguration<COURSE_EPISODE>
{
    public void Configure(EntityTypeBuilder<COURSE_EPISODE> builder)
    {
        builder.ToTable("COURSE_EPISODES", "CATALOG");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.SortOrder).IsRequired();
        builder.Property(e => e.MediaAssetId);
        builder.Property(e => e.DurationSeconds);
        builder.Property(e => e.IsFreePreview).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        // .WithMany(s => s.Episodes), not a bare .WithMany() — COURSE_SECTION.Episodes is a real
        // navigation property; see CourseSectionConfiguration's matching note for why an unreferenced
        // navigation makes EF invent a phantom second relationship with its own shadow FK.
        builder.HasOne<COURSE_SECTION>()
            .WithMany(s => s.Episodes)
            .HasForeignKey(e => e.SectionId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction, not Cascade — see this class's own doc comment for why (avoids the dual-cascade-path
        // DDL failure with the SectionId FK above).
        builder.HasOne<COURSE>()
            .WithMany()
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.NoAction);

        // Matches docs/DATABASE.md's "UQ(SectionId, SortOrder)" exactly.
        builder.HasIndex(e => new { e.SectionId, e.SortOrder }).IsUnique();

        builder.Property(e => e.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(e => e.UpdatedAtUtc).HasPrecision(3);
    }
}
