using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>EF Core mapping for <see cref="User"/> — see docs/DATABASE.md's "identity" section.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<USER>
{
    public void Configure(EntityTypeBuilder<USER> builder)
    {
        builder.ToTable("USERS", "IDENTITY");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();

        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.AvatarUrl).HasMaxLength(1000);
        builder.Property(u => u.PhoneNumber).HasMaxLength(32);

        // Enums are stored as their string name (not the numeric value), so the raw DB row stays
        // human-readable and doesn't silently break if enum members are reordered someday.
        // database.md leaves the string-vs-smallint+lookup choice open but says to pick one and
        // stick with it everywhere — this is the first enum in the codebase, so: every future enum
        // in any module should use the same `HasConversion<string>()` pattern.
        builder.Property(u => u.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(u => u.EmailConfirmedAtUtc).HasPrecision(3);
        builder.Property(u => u.LastLoginAtUtc).HasPrecision(3);

        builder.Property(u => u.TwoFactorEnabled).IsRequired();

        // SE-03 per-account override (docs/DATABASE.md identity section) — nullable int, no
        // HasMaxLength (not a string column); null = "use the system-wide default"
        // (ConcurrentSessionOptions.MaxConcurrentSessions), see MaxConcurrentSessionsOverride's own
        // doc comment.
        builder.Property(u => u.MaxConcurrentSessionsOverride);

        builder.Property(u => u.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(u => u.UpdatedAtUtc).HasPrecision(3);
        // CreatedBy/UpdatedBy: no FK to Users — the acting admin isn't necessarily the row's own
        // user, and self-referencing audit FKs add cascade-cycle complexity for no real benefit here.

        // Users <-> Roles: DATABASE.md's UserRoles table is just the two FK columns with a
        // composite PK and nothing else, so the implicit EF Core skip-navigation join (no hand-written
        // join entity) is the simpler, equally-correct choice here. Column names/PK order are
        // configured explicitly (rather than left to EF's default "RolesId"/"UserId" naming from the
        // `Roles` navigation) to match DATABASE.md's "UserRoles(UserId, RoleId) PK(UserId,RoleId)"
        // exactly.
        builder.HasMany(u => u.Roles)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "UserRoles",
                right => right.HasOne<ROLE>().WithMany().HasForeignKey("RoleId"),
                left => left.HasOne<USER>().WithMany().HasForeignKey("UserId"),
                join =>
                {
                    join.ToTable("USER_ROLES", "IDENTITY");
                    join.HasKey("UserId", "RoleId");
                });

        builder.Navigation(u => u.Roles)
            .HasField("_roles")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
