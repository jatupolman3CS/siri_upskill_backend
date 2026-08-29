using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Cms.Domain;

namespace Siri.Modules.Cms.Infrastructure;

public sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FEATURE_FLAG>
{
    public void Configure(EntityTypeBuilder<FEATURE_FLAG> builder)
    {
        builder.ToTable("FEATURE_FLAGS", "CMS");

        builder.HasKey(f => f.FEATURE_FLAG_ID);

        builder.Property(f => f.KEY)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(f => f.NAME)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(f => f.DESCRIPTION)
            .HasMaxLength(1000);

        builder.Property(f => f.IS_ENABLED)
            .IsRequired();

        builder.HasIndex(f => f.KEY)
            .IsUnique();
    }
}
