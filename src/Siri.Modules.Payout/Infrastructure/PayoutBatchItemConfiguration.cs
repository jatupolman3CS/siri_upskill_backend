using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="PAYOUT_BATCH_ITEM"/> — see docs/DATABASE.md's "payout" section and this
/// module's D-17 UPPERCASE-naming decision (docs/DECISIONS.md).
/// </summary>
public sealed class PayoutBatchItemConfiguration : IEntityTypeConfiguration<PAYOUT_BATCH_ITEM>
{
    public void Configure(EntityTypeBuilder<PAYOUT_BATCH_ITEM> builder)
    {
        builder.ToTable("PAYOUT_BATCH_ITEMS", "PAYOUT");

        builder.HasKey(x => x.PAYOUT_BATCH_ITEM_ID);
        builder.Property(x => x.PAYOUT_BATCH_ITEM_ID).HasColumnName("PAYOUT_BATCH_ITEM_ID");

        builder.Property(x => x.BATCH_ID).HasColumnName("BATCH_ID").IsRequired();
        // Cascade — composition child of its PAYOUT_BATCH
        builder.HasOne<PAYOUT_BATCH>()
            .WithMany(b => b.Items)
            .HasForeignKey(x => x.BATCH_ID)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.INSTRUCTOR_ID).HasColumnName("INSTRUCTOR_ID").IsRequired();
        builder.HasIndex(x => x.INSTRUCTOR_ID).HasDatabaseName("IX_PAYOUT_BATCH_ITEMS_INSTRUCTOR_ID");

        builder.Property(x => x.AMOUNT).HasColumnName("AMOUNT").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.WITHHOLDING_TAX_PERCENT).HasColumnName("WITHHOLDING_TAX_PERCENT").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.WITHHOLDING_TAX_AMOUNT).HasColumnName("WITHHOLDING_TAX_AMOUNT").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.NET_AMOUNT).HasColumnName("NET_AMOUNT").HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.STATUS).HasColumnName("STATUS").HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(x => x.TRANSFER_REF).HasColumnName("TRANSFER_REF").HasMaxLength(200);

        builder.Property(x => x.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(x => x.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
