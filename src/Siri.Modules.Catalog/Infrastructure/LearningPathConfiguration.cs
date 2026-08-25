using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class LearningPathConfiguration : IEntityTypeConfiguration<LearningPath>
{
    public void Configure(EntityTypeBuilder<LearningPath> builder)
    {
        builder.ToTable("LearningPaths", "catalog");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Slug).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.SortOrder).IsRequired();

        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.CreatedBy);
        builder.Property(p => p.UpdatedAtUtc);
        builder.Property(p => p.UpdatedBy);

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => new { p.IsActive, p.SortOrder });

        builder.HasMany(p => p.Items)
            .WithOne()
            .HasForeignKey(i => i.PathId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
