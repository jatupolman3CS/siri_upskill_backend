using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class CourseRequirementConfiguration : IEntityTypeConfiguration<COURSE_REQUIREMENT>
{
    public void Configure(EntityTypeBuilder<COURSE_REQUIREMENT> builder)
    {
        builder.ToTable("COURSE_REQUIREMENTS", "CATALOG");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Text).HasMaxLength(500).IsRequired();
        builder.Property(r => r.SortOrder).IsRequired();

        // Cascade: single incoming FK path to Courses (no CourseEpisodeConfiguration-style conflict).
        // .WithMany(c => c.Requirements), not a bare .WithMany() — COURSE.Requirements is a real
        // navigation property; see CourseSectionConfiguration's matching note for why an unreferenced
        // navigation makes EF invent a phantom second relationship with its own shadow FK.
        builder.HasOne<COURSE>()
            .WithMany(c => c.Requirements)
            .HasForeignKey(r => r.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.CourseId, r.SortOrder });

        builder.Property(r => r.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).HasPrecision(3);
    }
}
