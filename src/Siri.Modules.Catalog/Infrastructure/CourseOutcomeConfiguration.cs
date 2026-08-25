using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class CourseOutcomeConfiguration : IEntityTypeConfiguration<CourseOutcome>
{
    public void Configure(EntityTypeBuilder<CourseOutcome> builder)
    {
        builder.ToTable("CourseOutcomes", "catalog");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Text).HasMaxLength(500).IsRequired();
        builder.Property(o => o.SortOrder).IsRequired();

        // Cascade: single incoming FK path to Courses (no CourseEpisodeConfiguration-style conflict).
        // .WithMany(c => c.Outcomes), not a bare .WithMany() — Course.Outcomes is a real navigation
        // property; see CourseSectionConfiguration's matching note for why an unreferenced navigation
        // makes EF invent a phantom second relationship with its own shadow FK.
        builder.HasOne<Course>()
            .WithMany(c => c.Outcomes)
            .HasForeignKey(o => o.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(o => new { o.CourseId, o.SortOrder });

        builder.Property(o => o.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(o => o.UpdatedAtUtc).HasPrecision(3);
    }
}
