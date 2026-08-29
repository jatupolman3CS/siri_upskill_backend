using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WISHLIST_ITEM>
{
    public void Configure(EntityTypeBuilder<WISHLIST_ITEM> builder)
    {
        builder.ToTable("WISHLISTS", "CATALOG");
        builder.HasKey(w => new { w.UserId, w.CourseId });
        builder.Property(w => w.UserId).IsRequired();
        builder.Property(w => w.CourseId).IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();

        builder.HasIndex(w => w.UserId);
    }
}
