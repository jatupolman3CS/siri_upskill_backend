using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="REVENUE_SPLIT"/> — see docs/DATABASE.md's "payout" section and this
/// module's D-17 UPPERCASE-naming decision (docs/DECISIONS.md).
/// </summary>
public sealed class RevenueSplitConfiguration : IEntityTypeConfiguration<REVENUE_SPLIT>
{
    public void Configure(EntityTypeBuilder<REVENUE_SPLIT> builder)
    {
        builder.ToTable("REVENUE_SPLITS", "PAYOUT");

        builder.HasKey(x => x.REVENUE_SPLIT_ID);
        builder.Property(x => x.REVENUE_SPLIT_ID).HasColumnName("REVENUE_SPLIT_ID");

        builder.Property(x => x.ORDER_ITEM_ID).HasColumnName("ORDER_ITEM_ID").IsRequired();
        builder.HasIndex(x => x.ORDER_ITEM_ID).HasDatabaseName("IX_REVENUE_SPLITS_ORDER_ITEM_ID");

        builder.Property(x => x.INSTRUCTOR_ID).HasColumnName("INSTRUCTOR_ID").IsRequired();

        builder.Property(x => x.GROSS_AMOUNT).HasColumnName("GROSS_AMOUNT").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.PAYMENT_FEE_AMOUNT).HasColumnName("PAYMENT_FEE_AMOUNT").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.PLATFORM_FEE_AMOUNT).HasColumnName("PLATFORM_FEE_AMOUNT").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.INSTRUCTOR_AMOUNT).HasColumnName("INSTRUCTOR_AMOUNT").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.REVENUE_SHARE_PERCENT).HasColumnName("REVENUE_SHARE_PERCENT").HasPrecision(5, 2).IsRequired();

        // char(7), not nvarchar — same database.md carve-out CourseConfiguration.Currency already uses
        // for char(3), applied here to the 'YYYY-MM' period key.
        builder.Property(x => x.PERIOD_KEY).HasColumnName("PERIOD_KEY").HasColumnType("char(7)").IsRequired();

        builder.Property(x => x.STATUS).HasColumnName("STATUS").HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(x => x.PAYOUT_BATCH_ITEM_ID).HasColumnName("PAYOUT_BATCH_ITEM_ID");
        builder.HasIndex(x => x.PAYOUT_BATCH_ITEM_ID).HasDatabaseName("IX_REVENUE_SPLITS_PAYOUT_BATCH_ITEM_ID");

        // docs/DATABASE.md's "Index ที่ต้องมีตั้งแต่วันแรก": IX_RevenueSplits_Payout (InstructorId,
        // PeriodKey, Status) — the module's day-one query pattern is "this instructor's splits for this
        // period, filtered by status" (payout-batch assembly, instructor-facing statements).
        builder.HasIndex(x => new { x.INSTRUCTOR_ID, x.PERIOD_KEY, x.STATUS }).HasDatabaseName("IX_REVENUE_SPLITS_PAYOUT");

        builder.Property(x => x.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(x => x.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
