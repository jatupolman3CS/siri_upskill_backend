using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>EF Core mapping for <see cref="InstructorProfile"/> — see docs/DATABASE.md's "catalog" section.</summary>
public sealed class InstructorProfileConfiguration : IEntityTypeConfiguration<InstructorProfile>
{
    public void Configure(EntityTypeBuilder<InstructorProfile> builder)
    {
        builder.ToTable("InstructorProfiles", "catalog");

        builder.HasKey(p => p.Id);

        // No FK constraint to identity.Users — see InstructorProfile's own doc comment. Unique index
        // only, which is also what enforces "one profile per user, ever" (Resubmit reuses the row
        // instead of a second insert).
        builder.Property(p => p.UserId).IsRequired();
        builder.HasIndex(p => p.UserId).IsUnique();

        builder.Property(p => p.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Headline).HasMaxLength(200);
        builder.Property(p => p.Bio).HasMaxLength(2000).IsRequired();
        builder.Property(p => p.AvatarUrl).HasMaxLength(1000);

        builder.Property(p => p.RevenueSharePercent).HasPrecision(5, 2).IsRequired();

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.ApprovedAtUtc).HasPrecision(3);

        builder.Property(p => p.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).HasPrecision(3);
    }
}
