using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>EF Core mapping for <see cref="SecurityAudit"/> — see docs/DATABASE.md's "identity" section.</summary>
public sealed class SecurityAuditConfiguration : IEntityTypeConfiguration<SecurityAudit>
{
    public void Configure(EntityTypeBuilder<SecurityAudit> builder)
    {
        builder.ToTable("SecurityAudits", "identity");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.EventType).HasMaxLength(100).IsRequired();

        // Genuinely unbounded structured JSON (DATABASE.md: "Detail(json)") — the one intentional
        // exception to "always HasMaxLength on nvarchar columns".
        builder.Property(a => a.Detail).HasColumnType("nvarchar(max)");

        builder.Property(a => a.IpAddress).HasMaxLength(64);

        builder.Property(a => a.OccurredAtUtc).HasPrecision(3).IsRequired();

        // UserId is nullable (event may have no resolved user, e.g. failed login against an unknown
        // email) — Restrict so an audit row is never silently dropped by a cascade off its user.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
