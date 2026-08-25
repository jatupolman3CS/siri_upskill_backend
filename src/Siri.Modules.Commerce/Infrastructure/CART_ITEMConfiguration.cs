using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="CART_ITEM"/> — see docs/DATABASE.md's "commerce" section.</summary>
public sealed class CART_ITEMConfiguration : IEntityTypeConfiguration<CART_ITEM>
{
    public void Configure(EntityTypeBuilder<CART_ITEM> builder)
    {
        builder.ToTable("CART_ITEMS", "COMMERCE");

        builder.HasKey(ci => ci.CART_ITEM_ID);

        // Pure composition child — true composition child of CART per this scaffold task's own
        // instructions, so Cascade is correct here.
        builder.HasOne<CART>()
            .WithMany(c => c.CART_ITEMS)
            .HasForeignKey(ci => ci.CART_ID)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(ci => ci.ITEM_TYPE).HasConversion<string>().HasMaxLength(32).IsRequired();

        // No FK — polymorphic reference (Course or Bundle depending on ITEM_TYPE), see this property's
        // own doc comment on CART_ITEM.
        builder.Property(ci => ci.REF_ID).IsRequired();

        builder.Property(ci => ci.ADDED_AT_UTC).HasPrecision(3).IsRequired();
    }
}
