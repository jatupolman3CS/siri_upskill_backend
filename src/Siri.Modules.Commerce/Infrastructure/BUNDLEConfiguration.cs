using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="BUNDLE"/> — see docs/DATABASE.md's "commerce" section.</summary>
public sealed class BUNDLEConfiguration : IEntityTypeConfiguration<BUNDLE>
{
    public void Configure(EntityTypeBuilder<BUNDLE> builder)
    {
        builder.ToTable("BUNDLES", "COMMERCE");

        builder.HasKey(b => b.BUNDLE_ID);

        builder.Property(b => b.SLUG).HasMaxLength(200).IsRequired();
        builder.HasIndex(b => b.SLUG).IsUnique();

        builder.Property(b => b.TITLE).HasMaxLength(200).IsRequired();
        builder.Property(b => b.DESCRIPTION).HasMaxLength(4000);
        builder.Property(b => b.PRICE).HasPrecision(18, 2).IsRequired();
        builder.Property(b => b.IS_ACTIVE).IsRequired();
        builder.Property(b => b.STARTS_AT_UTC).HasPrecision(3);
        builder.Property(b => b.ENDS_AT_UTC).HasPrecision(3);

        builder.Navigation(b => b.BUNDLE_ITEMS).HasField("_bundleItems").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
