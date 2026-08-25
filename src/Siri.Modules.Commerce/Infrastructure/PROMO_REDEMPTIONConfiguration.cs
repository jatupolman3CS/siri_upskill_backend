using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="PROMO_REDEMPTION"/> — see docs/DATABASE.md's "commerce" section.
/// <para>
/// Two FKs, two different <c>OnDelete</c> behaviors: <see cref="PROMO_REDEMPTION.PROMO_CODE_ID"/> is
/// <c>Cascade</c> (this scaffold task's own instructions list PROMO_REDEMPTIONS as a "true composition
/// child of their parent", and the parent here is the promo code — a redemption record only means
/// anything in the context of the code it redeemed). <see cref="PROMO_REDEMPTION.ORDER_ID"/> is
/// <c>NoAction</c> instead, even though ORDERS is not itself one of the two parents this table is a
/// child "of" in the composition sense — ORDERS is one of this scaffold pass's four protected money
/// tables (Orders, Payments, Refunds, TaxInvoices), and a redemption record is part of that order's
/// permanent financial history, so it must not be reachable via a second cascade path either. (Two
/// independent Cascade FKs on the same table would not conflict here — PROMO_CODES and ORDERS do not
/// reference each other, so there is no shared-ancestor diamond for SQL Server to reject — but NoAction
/// on the ORDERS side is the deliberate choice regardless, for the money-table protection reason above,
/// not because of a cascade-path SQL error.)
/// </para>
/// </summary>
public sealed class PROMO_REDEMPTIONConfiguration : IEntityTypeConfiguration<PROMO_REDEMPTION>
{
    public void Configure(EntityTypeBuilder<PROMO_REDEMPTION> builder)
    {
        builder.ToTable("PROMO_REDEMPTIONS", "COMMERCE");

        builder.HasKey(r => r.PROMO_REDEMPTION_ID);

        builder.HasOne<PROMO_CODE>()
            .WithMany()
            .HasForeignKey(r => r.PROMO_CODE_ID)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ORDER>()
            .WithMany()
            .HasForeignKey(r => r.ORDER_ID)
            .OnDelete(DeleteBehavior.NoAction);

        // No FK — conceptual reference to identity.Users.Id only.
        builder.Property(r => r.USER_ID).IsRequired();

        builder.Property(r => r.REDEEMED_AT_UTC).HasPrecision(3).IsRequired();

        // docs/DATABASE.md: "UQ(PromoCodeId, OrderId)" — a code can be redeemed at most once per order.
        builder.HasIndex(r => new { r.PROMO_CODE_ID, r.ORDER_ID }).IsUnique();

        // Index for fast per-user redemption lookups and concurrency range locking.
        builder.HasIndex(r => new { r.PROMO_CODE_ID, r.USER_ID });
    }
}
