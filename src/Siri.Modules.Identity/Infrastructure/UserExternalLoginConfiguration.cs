using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>EF Core mapping for <see cref="USER_EXTERNAL_LOGIN"/>.</summary>
public sealed class UserExternalLoginConfiguration : IEntityTypeConfiguration<USER_EXTERNAL_LOGIN>
{
    public void Configure(EntityTypeBuilder<USER_EXTERNAL_LOGIN> builder)
    {
        builder.ToTable("USER_EXTERNAL_LOGINS", "IDENTITY");

        builder.HasKey(l => l.Id);

        // Same HasConversion<string>() convention as UserStatus / UserSecurityTokenPurpose.
        builder.Property(l => l.Provider)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // Google's `sub` is at most 255 ASCII characters per the OpenID Connect spec.
        builder.Property(l => l.ProviderSubject).HasMaxLength(255).IsRequired();
        builder.Property(l => l.ProviderEmail).HasMaxLength(256).IsRequired();

        builder.Property(l => l.LinkedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(l => l.LastLoginAtUtc).HasPrecision(3).IsRequired();

        // One external account maps to exactly one local user.
        builder.HasIndex(l => new { l.Provider, l.ProviderSubject })
            .IsUnique()
            .HasDatabaseName("UX_USER_EXTERNAL_LOGINS_PROVIDER_SUBJECT");

        builder.HasIndex(l => l.UserId)
            .HasDatabaseName("IX_USER_EXTERNAL_LOGINS_USER_ID");

        // Restrict, like every other Identity FK to Users: auth rows never disappear via a cascade.
        builder.HasOne<USER>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
