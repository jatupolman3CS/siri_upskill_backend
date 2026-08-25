using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>EF Core mapping for <see cref="RefreshToken"/> — see docs/DATABASE.md's "identity" section.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", "identity");

        builder.HasKey(t => t.Id);

        // Only the hash is ever persisted (security.md) — SHA-256 hex is 64 chars, leave headroom
        // for a different hash algorithm later without another migration.
        builder.Property(t => t.TokenHash).HasMaxLength(256).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.ExpiresAtUtc).HasPrecision(3).IsRequired();
        builder.Property(t => t.RevokedAtUtc).HasPrecision(3);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserSession>()
            .WithMany()
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing "rotated into" pointer — Restrict, not Cascade, so deleting a token can
        // never accidentally cascade through an entire rotation chain (tokens are revoked, never
        // hard-deleted, in practice, but the FK still shouldn't allow a cascade path to exist).
        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(t => t.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
