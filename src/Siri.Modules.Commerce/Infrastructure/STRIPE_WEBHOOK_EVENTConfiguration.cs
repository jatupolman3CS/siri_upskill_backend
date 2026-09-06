using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="STRIPE_WEBHOOK_EVENT"/> — see docs/DATABASE.md's "commerce"
/// section.</summary>
public sealed class STRIPE_WEBHOOK_EVENTConfiguration : IEntityTypeConfiguration<STRIPE_WEBHOOK_EVENT>
{
    public void Configure(EntityTypeBuilder<STRIPE_WEBHOOK_EVENT> builder)
    {
        builder.ToTable("STRIPE_WEBHOOK_EVENTS", "COMMERCE");

        builder.HasKey(e => e.STRIPE_WEBHOOK_EVENT_ID);

        // The idempotency guard — security.md: "Webhook ต้อง idempotent (unique index บน provider event id)".
        builder.Property(e => e.STRIPE_EVENT_ID).HasMaxLength(255).IsRequired();
        builder.HasIndex(e => e.STRIPE_EVENT_ID).IsUnique();

        builder.Property(e => e.EVENT_TYPE).HasMaxLength(100).IsRequired();

        // nvarchar(max) — the one deliberate exception to "always HasMaxLength" in this whole scaffold
        // pass. See STRIPE_WEBHOOK_EVENT.PAYLOAD_JSON's own doc comment for why: raw Stripe payloads are
        // provider-sized, not ours to cap, and this is the one copy a payment dispute might need verbatim.
        builder.Property(e => e.PAYLOAD_JSON).HasColumnType("text").IsRequired();

        builder.Property(e => e.RECEIVED_AT_UTC).HasPrecision(3).IsRequired();
        builder.Property(e => e.PROCESSED_AT_UTC).HasPrecision(3);
        builder.Property(e => e.PROCESS_RESULT).HasMaxLength(500);
    }
}
