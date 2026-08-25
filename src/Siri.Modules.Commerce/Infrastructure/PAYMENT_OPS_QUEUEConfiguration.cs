using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="PAYMENT_OPS_QUEUE"/> — see docs/DATABASE.md's "commerce"
/// section. Table name matches the class name exactly (<c>PAYMENT_OPS_QUEUE</c>, no trailing "S") — see
/// that entity's own doc comment for why this one breaks the module's usual singular-class/plural-table
/// split.</summary>
public sealed class PAYMENT_OPS_QUEUEConfiguration : IEntityTypeConfiguration<PAYMENT_OPS_QUEUE>
{
    public void Configure(EntityTypeBuilder<PAYMENT_OPS_QUEUE> builder)
    {
        builder.ToTable("PAYMENT_OPS_QUEUE", "COMMERCE");

        builder.HasKey(q => q.PAYMENT_OPS_QUEUE_ID);

        // References PAYMENTS (a protected money table) — NoAction, not Cascade, same reasoning as
        // PAYMENTConfiguration's own ORDER_ID FK: an ops-triage entry must not vanish (or, worse, silently
        // cascade-delete) just because something touched the payment it is about.
        builder.HasOne<PAYMENT>()
            .WithMany()
            .HasForeignKey(q => q.PAYMENT_ID)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(q => q.REASON).HasMaxLength(200).IsRequired();
        builder.Property(q => q.STATUS).HasConversion<string>().HasMaxLength(32).IsRequired();

        // No FK — conceptual references to identity.Users.Id only (see these properties' own doc
        // comments on PAYMENT_OPS_QUEUE).
        builder.Property(q => q.ASSIGNED_TO_USER_ID);
        builder.Property(q => q.RESOLVED_BY_USER_ID);

        builder.Property(q => q.RESOLVED_AT_UTC).HasPrecision(3);
        builder.Property(q => q.NOTE).HasMaxLength(1000);
    }
}
