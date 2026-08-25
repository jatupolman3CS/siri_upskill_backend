using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="PROMO_CODE"/> — see docs/DATABASE.md's "commerce" section.</summary>
public sealed class PROMO_CODEConfiguration : IEntityTypeConfiguration<PROMO_CODE>
{
    public void Configure(EntityTypeBuilder<PROMO_CODE> builder)
    {
        builder.ToTable("PROMO_CODES", "COMMERCE");

        builder.HasKey(p => p.PROMO_CODE_ID);

        builder.Property(p => p.CODE).HasMaxLength(32).IsRequired();
        builder.HasIndex(p => p.CODE).IsUnique();

        builder.Property(p => p.DISCOUNT_TYPE).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.DISCOUNT_VALUE).HasPrecision(18, 2).IsRequired();

        builder.Property(p => p.MAX_REDEMPTIONS).IsRequired();
        builder.Property(p => p.REDEEMED_COUNT).IsRequired();
        builder.Property(p => p.MAX_PER_USER).IsRequired();
        builder.Property(p => p.MIN_ORDER_AMOUNT).HasPrecision(18, 2).IsRequired();

        builder.Property(p => p.STARTS_AT_UTC).HasPrecision(3).IsRequired();
        builder.Property(p => p.ENDS_AT_UTC).HasPrecision(3).IsRequired();

        builder.Property(p => p.SCOPE).HasConversion<string>().HasMaxLength(32).IsRequired();

        // No FK — polymorphic reference (Category/Course/Bundle depending on SCOPE), see this property's
        // own doc comment on PROMO_CODE.
        builder.Property(p => p.SCOPE_REF_ID);

        builder.Property(p => p.IS_ACTIVE).IsRequired();

        // Concurrency token — admins editing terms while redemptions may be happening concurrently, same
        // reasoning as CourseConfiguration.RowVersion.
        builder.Property(p => p.ROW_VERSION).IsRowVersion();
    }
}
