using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>EF Core mapping for <see cref="UserSession"/> — see docs/DATABASE.md's "identity" section.</summary>
public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions", "identity");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.DeviceId).HasMaxLength(200).IsRequired();
        builder.Property(s => s.DeviceName).HasMaxLength(200);
        builder.Property(s => s.UserAgent).HasMaxLength(500);
        builder.Property(s => s.IpAddress).HasMaxLength(64);
        builder.Property(s => s.RevokeReason).HasMaxLength(200);

        builder.Property(s => s.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(s => s.LastSeenAtUtc).HasPrecision(3).IsRequired();
        builder.Property(s => s.RevokedAtUtc).HasPrecision(3);

        // Computed, not stored — no setter and no backing field, so EF must not try to map it.
        builder.Ignore(s => s.IsActive);

        // DATABASE.md calls this composite index out by name: "IX(UserId, RevokedAtUtc) -- ใช้บังคับ
        // concurrent login limit" (used by the later concurrent-session-limit enforcement task).
        builder.HasIndex(s => new { s.UserId, s.RevokedAtUtc })
            .HasDatabaseName("IX_UserSessions_UserId_RevokedAtUtc");

        // DATABASE.md's separate "Index ที่ต้องมีตั้งแต่วันแรก" list also calls for a filtered index
        // over just the active sessions, for the same concurrent-login-limit query pattern:
        // "IX_UserSessions_Active (UserId) WHERE RevokedAtUtc IS NULL -- filtered index".
        builder.HasIndex(s => s.UserId)
            .HasDatabaseName("IX_UserSessions_Active")
            .HasFilter("[RevokedAtUtc] IS NULL");

        // No navigation property back on User — sessions are looked up by UserId, not loaded as
        // part of the User aggregate. Restrict (not the EF default Cascade) because auth/session
        // rows are never meant to disappear via a cascade off some other delete.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
