using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="PAYMENT"/> — see docs/DATABASE.md's "commerce" section.</summary>
public sealed class PAYMENTConfiguration : IEntityTypeConfiguration<PAYMENT>
{
    public void Configure(EntityTypeBuilder<PAYMENT> builder)
    {
        builder.ToTable("PAYMENTS", "COMMERCE");

        builder.HasKey(p => p.PAYMENT_ID);

        // Money table — NoAction, never Cascade, per this scaffold task's own instructions (Orders,
        // Payments, Refunds, TaxInvoices must never disappear as a side effect of deleting something
        // else). EF's convention also adds a non-unique index on ORDER_ID for this relationship.
        builder.HasOne<ORDER>()
            .WithMany()
            .HasForeignKey(p => p.ORDER_ID)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(p => p.METHOD).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.PROVIDER).HasMaxLength(32).IsRequired();

        builder.Property(p => p.PROVIDER_PAYMENT_INTENT_ID).HasMaxLength(255).IsRequired();
        builder.HasIndex(p => p.PROVIDER_PAYMENT_INTENT_ID).IsUnique();

        builder.Property(p => p.AMOUNT).HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.STATUS).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.SUCCEEDED_AT_UTC).HasPrecision(3);
        builder.Property(p => p.FAILURE_REASON).HasMaxLength(500);
        builder.Property(p => p.CREATED_AT_UTC).HasPrecision(3).IsRequired();
    }
}
