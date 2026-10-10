using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="PAYMENT_AMOUNT_OVERRIDE"/> — an append-only audit log that doubles
/// as the configuration (see the entity's doc comment).</summary>
public sealed class PAYMENT_AMOUNT_OVERRIDEConfiguration : IEntityTypeConfiguration<PAYMENT_AMOUNT_OVERRIDE>
{
    public void Configure(EntityTypeBuilder<PAYMENT_AMOUNT_OVERRIDE> builder)
    {
        builder.ToTable("PAYMENT_AMOUNT_OVERRIDES", "COMMERCE");

        builder.HasKey(o => o.PAYMENT_AMOUNT_OVERRIDE_ID);

        builder.Property(o => o.IS_ENABLED).IsRequired();
        builder.Property(o => o.OVERRIDE_AMOUNT).HasPrecision(18, 2).IsRequired();
        builder.Property(o => o.REASON).HasMaxLength(PAYMENT_AMOUNT_OVERRIDE.MaxReasonLength).IsRequired();

        // No FK — conceptual reference to identity.Users.Id only (see the property's own doc comment).
        builder.Property(o => o.CHANGED_BY_USER_ID).IsRequired();
        builder.Property(o => o.CHANGED_AT_UTC).HasPrecision(3).IsRequired();

        // "Current setting" = newest row, read on every payment creation; history is listed newest first.
        builder.HasIndex(o => o.CHANGED_AT_UTC).IsDescending();
    }
}
