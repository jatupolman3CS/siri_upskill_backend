using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>EF Core mapping for <see cref="SECURITY_AUDIT"/> — see docs/DATABASE.md's "identity" section.</summary>
public sealed class SecurityAuditConfiguration : IEntityTypeConfiguration<SECURITY_AUDIT>
{
    public void Configure(EntityTypeBuilder<SECURITY_AUDIT> builder)
    {
        builder.ToTable("SECURITY_AUDITS", "IDENTITY");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.EventType).HasMaxLength(100).IsRequired();

        // Genuinely unbounded structured JSON (DATABASE.md: "Detail(json)") — the one intentional
        // exception to "always HasMaxLength on nvarchar columns".
        builder.Property(a => a.Detail).HasColumnType("text");

        builder.Property(a => a.IpAddress).HasMaxLength(64);

        builder.Property(a => a.OccurredAtUtc).HasPrecision(3).IsRequired();

        // UserId is nullable (event may have no resolved user, e.g. failed login against an unknown
        // email) — Restrict so an audit row is never silently dropped by a cascade off its user.
        builder.HasOne<USER>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
