using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="Role"/>, plus the seed data for the four fixed system roles.
/// This is genuinely part of the schema (fixed lookup data), unlike developer/demo test data —
/// seeding real test users/courses is a separate, later task.
/// </summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", "identity");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(50).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();

        // HasData needs static, deterministic values (never Guid.CreateVersion7()/UuidV7.NewId() —
        // migrations must generate identical SQL every time), so it takes the fixed ids from
        // Role itself rather than constructing entities. Plain anonymous objects (not `new Role()`,
        // which has no public constructor) so this works regardless of Role's own property
        // accessibility.
        builder.HasData(
            new { Id = Role.LearnerId, Name = Role.LearnerName },
            new { Id = Role.InstructorId, Name = Role.InstructorName },
            new { Id = Role.AdminId, Name = Role.AdminName },
            new { Id = Role.SuperAdminId, Name = Role.SuperAdminName });
    }
}
