using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="CART"/> — see docs/DATABASE.md's "commerce" section.</summary>
public sealed class CARTConfiguration : IEntityTypeConfiguration<CART>
{
    public void Configure(EntityTypeBuilder<CART> builder)
    {
        builder.ToTable("CARTS", "COMMERCE");

        builder.HasKey(c => c.CART_ID);

        // No FK — conceptual reference to identity.Users.Id only. Unique: one cart per user, ever.
        builder.Property(c => c.USER_ID).IsRequired();
        builder.HasIndex(c => c.USER_ID).IsUnique();

        builder.Property(c => c.UPDATED_AT_UTC).HasPrecision(3).IsRequired();

        builder.Navigation(c => c.CART_ITEMS).HasField("_cartItems").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
