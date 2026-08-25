using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="FLASH_SALE"/> — see docs/DATABASE.md's "commerce" section.</summary>
public sealed class FLASH_SALEConfiguration : IEntityTypeConfiguration<FLASH_SALE>
{
    public void Configure(EntityTypeBuilder<FLASH_SALE> builder)
    {
        builder.ToTable("FLASH_SALES", "COMMERCE");

        builder.HasKey(f => f.FLASH_SALE_ID);

        builder.Property(f => f.TITLE).HasMaxLength(200).IsRequired();
        builder.Property(f => f.STARTS_AT_UTC).HasPrecision(3).IsRequired();
        builder.Property(f => f.ENDS_AT_UTC).HasPrecision(3).IsRequired();
        builder.Property(f => f.IS_ACTIVE).IsRequired();

        builder.Navigation(f => f.FLASH_SALE_ITEMS).HasField("_flashSaleItems").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
