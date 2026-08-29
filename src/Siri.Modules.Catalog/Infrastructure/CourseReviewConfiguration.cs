using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class CourseReviewConfiguration : IEntityTypeConfiguration<COURSE_REVIEW>
{
    public void Configure(EntityTypeBuilder<COURSE_REVIEW> builder)
    {
        builder.ToTable("COURSE_REVIEWS", "CATALOG");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.CourseId)
            .IsRequired();

        builder.Property(r => r.UserId)
            .IsRequired();

        builder.Property(r => r.Rating)
            .IsRequired();

        builder.Property(r => r.Comment)
            .HasMaxLength(2000);

        builder.Property(r => r.IsPublished)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(r => r.CreatedAtUtc)
            .IsRequired();

        builder.Property(r => r.UpdatedAtUtc);

        builder.HasIndex(r => new { r.CourseId, r.UserId })
            .IsUnique();

        builder.HasIndex(r => r.CourseId);
    }
}
