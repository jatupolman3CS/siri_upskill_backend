using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="CourseEpisode"/>.
/// <para>
/// <see cref="CourseEpisode.CourseId"/> and <see cref="CourseEpisode.SectionId"/> both ultimately root
/// at <see cref="Course"/> — <c>SectionId → CourseSections.Id</c> is the real ownership edge
/// (<c>Cascade</c>), but the denormalized <c>CourseId → Courses.Id</c> FK <em>must</em> be
/// <c>Restrict</c>/<c>NoAction</c>, not <c>Cascade</c>: SQL Server rejects a schema where two different
/// <c>Cascade</c> paths both reach the same descendant table ("may cause cycles or multiple cascade
/// paths") — here, <c>CourseEpisodes</c> is reachable both via <c>Courses → CourseSections → CourseEpisodes</c>
/// and directly via <c>Courses → CourseEpisodes</c>. <c>NoAction</c> on the direct edge does not block
/// cascading — episode rows still get removed through the real Section→Episode chain within the same
/// cascading delete statement.
/// </para>
/// <see cref="CourseEpisode.MediaAssetId"/> has no FK constraint at all — see
/// <see cref="CourseEpisode"/>'s own doc comment and <c>CourseConfiguration</c>'s (same reasoning as
/// <c>TrailerMediaAssetId</c>: cross-module, never gets a DB-level FK).
/// </summary>
public sealed class CourseEpisodeConfiguration : IEntityTypeConfiguration<CourseEpisode>
{
    public void Configure(EntityTypeBuilder<CourseEpisode> builder)
    {
        builder.ToTable("CourseEpisodes", "catalog");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.SortOrder).IsRequired();
        builder.Property(e => e.MediaAssetId);
        builder.Property(e => e.DurationSeconds);
        builder.Property(e => e.IsFreePreview).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        // .WithMany(s => s.Episodes), not a bare .WithMany() — CourseSection.Episodes is a real
        // navigation property; see CourseSectionConfiguration's matching note for why an unreferenced
        // navigation makes EF invent a phantom second relationship with its own shadow FK.
        builder.HasOne<CourseSection>()
            .WithMany(s => s.Episodes)
            .HasForeignKey(e => e.SectionId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction, not Cascade — see this class's own doc comment for why (avoids the dual-cascade-path
        // DDL failure with the SectionId FK above).
        builder.HasOne<Course>()
            .WithMany()
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.NoAction);

        // Matches docs/DATABASE.md's "UQ(SectionId, SortOrder)" exactly.
        builder.HasIndex(e => new { e.SectionId, e.SortOrder }).IsUnique();

        builder.Property(e => e.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(e => e.UpdatedAtUtc).HasPrecision(3);
    }
}
