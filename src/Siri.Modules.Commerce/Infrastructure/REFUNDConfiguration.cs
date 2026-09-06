using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="REFUND"/> — see docs/DATABASE.md's "commerce" section and
/// docs/DECISIONS.md D-17's gap-fill (approval-workflow columns).</summary>
public sealed class REFUNDConfiguration : IEntityTypeConfiguration<REFUND>
{
    public void Configure(EntityTypeBuilder<REFUND> builder)
    {
        builder.ToTable("REFUNDS", "COMMERCE");

        builder.HasKey(r => r.REFUND_ID);

        // Money table — NoAction, never Cascade, same reasoning as PAYMENTConfiguration's own ORDER_ID FK.
        builder.HasOne<PAYMENT>()
            .WithMany()
            .HasForeignKey(r => r.PAYMENT_ID)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(r => r.AMOUNT).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.REASON).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.STATUS).HasConversion<string>().HasMaxLength(32).IsRequired();

        // No FK — conceptual reference to identity.Users.Id only.
        builder.Property(r => r.REQUESTED_BY_USER_ID).IsRequired();
        builder.Property(r => r.REQUESTED_AT_UTC).HasPrecision(3).IsRequired();

        // docs/DATABASE.md's day-one-style "my refund requests" access pattern — same shape as
        // ORDERConfiguration's IX_ORDERS_USER_HISTORY.
        builder.HasIndex(r => new { r.REQUESTED_BY_USER_ID, r.REQUESTED_AT_UTC }).HasDatabaseName("IX_REFUNDS_REQUESTED_BY_USER");

        // No FK — conceptual reference to identity.Users.Id only.
        builder.Property(r => r.DECIDED_BY_USER_ID);
        builder.Property(r => r.DECIDED_AT_UTC).HasPrecision(3);
        builder.Property(r => r.DECISION_NOTE).HasMaxLength(1000);

        builder.Property(r => r.STRIPE_REFUND_ID).HasMaxLength(255);
        // Filtered so only rows that actually have a Stripe refund id compete for uniqueness — every
        // Requested/Approved/Rejected row has STRIPE_REFUND_ID = NULL, and SQL Server's default unique
        // index treats multiple NULLs as distinct anyway, but the filter makes the intent explicit
        // (matches CourseConfiguration's own filtered-unique-index precedent for a similar "not every row
        // has one yet" column).
        builder.HasIndex(r => r.STRIPE_REFUND_ID).IsUnique().HasFilter("\"STRIPE_REFUND_ID\" IS NOT NULL");

        builder.Property(r => r.COMPLETED_AT_UTC).HasPrecision(3);
    }
}
