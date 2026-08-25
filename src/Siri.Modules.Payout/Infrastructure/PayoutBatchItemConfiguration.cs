using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="PAYOUT_BATCH_ITEM"/> — see docs/DATABASE.md's "payout" section and this
/// module's D-17 UPPERCASE-naming decision (docs/DECISIONS.md).
/// <para>
/// <see cref="PAYOUT_BATCH_ITEM.BATCH_ID"/> is this module's one <c>Cascade</c> FK — see
/// <see cref="PAYOUT_BATCH_ITEM"/>'s own doc comment for why (true composition child of its
/// <see cref="PAYOUT_BATCH"/>, same reasoning Catalog's <c>CourseEpisode</c>→<c>CourseSection</c>'s FK
/// uses). <see cref="PAYOUT_BATCH_ITEM.INSTRUCTOR_ID"/> gets no <c>HasOne()</c>/FK at all — see
/// <see cref="REVENUE_SPLIT"/>'s own doc comment for why a cross-module/cross-schema FK is never added in
/// this module.
/// </para>
/// </summary>
public sealed class PayoutBatchItemConfiguration : IEntityTypeConfiguration<PAYOUT_BATCH_ITEM>
{
    public void Configure(EntityTypeBuilder<PAYOUT_BATCH_ITEM> builder)
    {
        builder.ToTable("PAYOUT_BATCH_ITEMS", "PAYOUT");

        builder.HasKey(x => x.PAYOUT_BATCH_ITEM_ID);
        builder.Property(x => x.PAYOUT_BATCH_ITEM_ID).HasColumnName("PAYOUT_BATCH_ITEM_ID");

        builder.Property(x => x.BATCH_ID).HasColumnName("BATCH_ID").IsRequired();
        // Cascade — the one exception in this module; see this class's own doc comment.
        builder.HasOne<PAYOUT_BATCH>()
            .WithMany(b => b.Items)
            .HasForeignKey(x => x.BATCH_ID)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.INSTRUCTOR_ID).HasColumnName("INSTRUCTOR_ID").IsRequired();
        // Serves the "this instructor's items across batches" lookup — same rationale as every other
        // INSTRUCTOR_ID index in this module.
        builder.HasIndex(x => x.INSTRUCTOR_ID).HasDatabaseName("IX_PAYOUT_BATCH_ITEMS_INSTRUCTOR_ID");

        builder.Property(x => x.AMOUNT).HasColumnName("AMOUNT").HasPrecision(18, 2).IsRequired();
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
