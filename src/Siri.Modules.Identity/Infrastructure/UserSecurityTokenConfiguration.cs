using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>EF Core mapping for <see cref="USER_SECURITY_TOKEN"/> — see docs/DATABASE.md's "identity" section.</summary>
public sealed class UserSecurityTokenConfiguration : IEntityTypeConfiguration<USER_SECURITY_TOKEN>
{
    public void Configure(EntityTypeBuilder<USER_SECURITY_TOKEN> builder)
    {
        builder.ToTable("USER_SECURITY_TOKENS", "IDENTITY");

        builder.HasKey(t => t.Id);

        // Same reasoning/width as RefreshTokenConfiguration.TokenHash: only the hash is ever
        // persisted (security.md), SHA-256 hex is 64 chars, 256 leaves headroom for a different hash
        // algorithm later without another migration.
        builder.Property(t => t.TokenHash).HasMaxLength(256).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Same HasConversion<string>() convention UserStatus established as the first enum in the
        // codebase (database.md: pick string-vs-smallint once, stay consistent everywhere).
        builder.Property(t => t.Purpose)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.ExpiresAtUtc).HasPrecision(3).IsRequired();
        builder.Property(t => t.ConsumedAtUtc).HasPrecision(3);

        // Computed, not stored — no setter and no backing field, so EF must not try to map it (same
        // pattern as UserSessionConfiguration.IsActive).
        builder.Ignore(t => t.IsConsumed);

        // Supports the "find the still-valid token for this user+purpose" lookup pattern the task
        // asked for (relevant once P0-21/password-reset or a resend-confirmation feature needs to
        // look up-or-invalidate an existing token rather than only ever inserting new ones) — same
        // filtered-index shape as UserSessionConfiguration's "IX_UserSessions_Active".
        builder.HasIndex(t => new { t.UserId, t.Purpose })
            .HasDatabaseName("IX_USER_SECURITY_TOKENS_ACTIVE")
            .HasFilter("\"CONSUMED_AT_UTC\" IS NULL");

        // Restrict (not the EF default Cascade) — same reasoning as every other Identity FK to
        // Users: an auth/security row must never disappear via a cascade off some other delete.
        builder.HasOne<USER>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
