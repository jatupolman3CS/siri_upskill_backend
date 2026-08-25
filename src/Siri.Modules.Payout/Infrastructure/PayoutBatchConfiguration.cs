using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="PAYOUT_BATCH"/> — see docs/DATABASE.md's "payout" section and this
/// module's D-17 UPPERCASE-naming decision (docs/DECISIONS.md).
/// <para>
/// <see cref="PAYOUT_BATCH.EXECUTED_BY_USER_ID"/> gets no <c>HasOne()</c>/FK at all — see
/// <see cref="REVENUE_SPLIT"/>'s own doc comment for why a cross-module/cross-schema FK is never added in
/// this module. The FK *side* of <see cref="PAYOUT_BATCH.Items"/> (→ <see cref="PAYOUT_BATCH_ITEM.BATCH_ID"/>,
/// <c>Cascade</c>) is configured from <see cref="PayoutBatchItemConfiguration"/> instead — the "many" side
/// — matching Catalog's <c>CourseConfiguration</c>/<c>CourseSectionConfiguration</c> split exactly. What
/// belongs here is telling EF how to *read* <see cref="PAYOUT_BATCH.Items"/>: it is a real navigation
/// property computed from a private backing field
/// (<c>PAYOUT_BATCH._items</c>), not a plain auto-property, so without this <c>.Navigation(...)</c> call
/// EF Core's convention discovery would find the navigation independently and create a second, phantom
/// relationship with its own shadow FK column — the exact bug Catalog's <c>Course</c>/<c>CourseSection</c>
/// hit for real during P1-02 (caught only by reading the generated migration in full before applying it;
/// see that task's notes) and is now known to guard against from the start here instead.
/// </para>
/// </summary>
public sealed class PayoutBatchConfiguration : IEntityTypeConfiguration<PAYOUT_BATCH>
{
    public void Configure(EntityTypeBuilder<PAYOUT_BATCH> builder)
    {
        builder.ToTable("PAYOUT_BATCHES", "PAYOUT");

        builder.HasKey(x => x.PAYOUT_BATCH_ID);
        builder.Property(x => x.PAYOUT_BATCH_ID).HasColumnName("PAYOUT_BATCH_ID");

        builder.Property(x => x.PERIOD_KEY).HasColumnName("PERIOD_KEY").HasColumnType("char(7)").IsRequired();
        // Non-unique — unlike REVENUE_SPLIT/INSTRUCTOR_PAYOUT_ACCOUNT this module has no stated
        // one-batch-per-period invariant (a Failed batch plausibly gets superseded by a fresh retry batch
        // for the same period; deciding whether that should instead reuse the same row is later-task
        // business logic, not a naming/schema concern this scaffold pass should pre-decide). Still indexed
        // for the obvious "batches for period X" admin-list query.
        builder.HasIndex(x => x.PERIOD_KEY).HasDatabaseName("IX_PAYOUT_BATCHES_PERIOD_KEY");

        builder.Property(x => x.TOTAL_AMOUNT).HasColumnName("TOTAL_AMOUNT").HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.STATUS).HasColumnName("STATUS").HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(x => x.EXECUTED_AT_UTC).HasColumnName("EXECUTED_AT_UTC").HasPrecision(3);
        builder.Property(x => x.EXECUTED_BY_USER_ID).HasColumnName("EXECUTED_BY_USER_ID");

        builder.Property(x => x.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(x => x.UpdatedBy).HasColumnName("UPDATED_BY");

        // See this class's own doc comment — the read side of the Items navigation only; the FK/Cascade
        // itself is configured from PayoutBatchItemConfiguration.
        builder.Navigation(x => x.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
