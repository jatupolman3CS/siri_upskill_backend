using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class LearningPathItemConfiguration : IEntityTypeConfiguration<LEARNING_PATH_ITEM>
{
    public void Configure(EntityTypeBuilder<LEARNING_PATH_ITEM> builder)
    {
        builder.ToTable("LEARNING_PATH_ITEMS", "CATALOG");

        builder.HasKey(i => new { i.PathId, i.CourseId });

        builder.Property(i => i.PathId).IsRequired();
        builder.Property(i => i.CourseId).IsRequired();
        builder.Property(i => i.SortOrder).IsRequired();

        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.CreatedBy);
        builder.Property(i => i.UpdatedAtUtc);
        builder.Property(i => i.UpdatedBy);

        builder.HasIndex(i => new { i.PathId, i.SortOrder });

        builder.HasOne<COURSE>()
            .WithMany()
            .HasForeignKey(i => i.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
