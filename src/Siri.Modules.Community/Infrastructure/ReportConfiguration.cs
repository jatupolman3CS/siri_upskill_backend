using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Community.Domain;

namespace Siri.Modules.Community.Infrastructure;

/// <summary>EF Core mapping for <see cref="REPORT"/> — see docs/DATABASE.md's "community" section and
/// <see cref="DiscussionConfiguration"/>'s own doc comment for the D-17 UPPERCASE column-naming mechanics
/// (identical here — no naming-convention package installed, so only the four
/// <see cref="Siri.Persistence.Conventions.IAuditable"/> properties need an explicit
/// <c>.HasColumnName(...)</c> override).</summary>
public sealed class ReportConfiguration : IEntityTypeConfiguration<REPORT>
{
    public void Configure(EntityTypeBuilder<REPORT> builder)
    {
        builder.ToTable("REPORTS", "community");

        builder.HasKey(r => r.REPORT_ID);

        builder.Property(r => r.DISCUSSION_ID).IsRequired();
        builder.Property(r => r.REPORTED_BY_USER_ID).IsRequired();

        builder.Property(r => r.REASON).HasMaxLength(500).IsRequired();
        builder.Property(r => r.STATUS).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.RESOLVED_AT_UTC).HasPrecision(3);

        // FK to DISCUSSIONS — NoAction (task-specified), not Cascade: a REPORT is an independent
        // moderation record about a DISCUSSION, not something the discussion owns/composes (contrast
        // e.g. CourseOutcome's genuine Cascade composition of Course) — same "NoAction for a same-schema
        // reference that isn't ownership" reasoning as catalog.Courses.InstructorId ->
        // catalog.InstructorProfiles (see Siri.Modules.Catalog.Infrastructure.CourseConfiguration's own
        // doc comment). Bare .WithMany() — DISCUSSION has no Reports navigation collection.
        builder.HasOne<DISCUSSION>()
            .WithMany()
            .HasForeignKey(r => r.DISCUSSION_ID)
            .OnDelete(DeleteBehavior.NoAction);

        // No FK to identity.Users (REPORTED_BY_USER_ID) — cross-module/cross-schema, same reasoning as
        // DISCUSSION.USER_ID (see DiscussionConfiguration's own doc comment).

        // Serves the admin moderation queue (IReportRepository.ListPendingAsync — filters
        // Status == Pending, oldest-first). EF's convention also adds a non-unique index on
        // DISCUSSION_ID for the FK above (no existing index already covers it as a leftmost prefix).
        builder.HasIndex(r => new { r.STATUS, r.CreatedAtUtc });

        // ---- IAuditable: UPPERCASE column, PascalCase C# property — REPORT is not ISoftDelete (see
        // REPORT's own doc comment), but every main table still gets the universal audit columns per
        // database.md's "กติกาทั่วไป" table. ------------------------------------------------------------
        builder.Property(r => r.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(r => r.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(r => r.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
