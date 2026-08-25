using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class LearningPathItemConfiguration : IEntityTypeConfiguration<LearningPathItem>
{
    public void Configure(EntityTypeBuilder<LearningPathItem> builder)
    {
        builder.ToTable("LearningPathItems", "catalog");

        builder.HasKey(i => new { i.PathId, i.CourseId });

        builder.Property(i => i.PathId).IsRequired();
        builder.Property(i => i.CourseId).IsRequired();
        builder.Property(i => i.SortOrder).IsRequired();

        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.CreatedBy);
        builder.Property(i => i.UpdatedAtUtc);
        builder.Property(i => i.UpdatedBy);

        builder.HasIndex(i => new { i.PathId, i.SortOrder });

        builder.HasOne<Course>()
            .WithMany()
            .HasForeignKey(i => i.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
