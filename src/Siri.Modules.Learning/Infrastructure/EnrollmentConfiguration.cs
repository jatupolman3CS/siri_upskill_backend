using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="ENROLLMENT"/> — see docs/DATABASE.md's "learning" section and this
/// module's D-17 UPPERCASE-naming decision (docs/DECISIONS.md).
/// <para>
/// <see cref="ENROLLMENT.USER_ID"/>/<see cref="ENROLLMENT.COURSE_ID"/>/<see cref="ENROLLMENT.ORDER_ID"/>
/// get no <c>HasOne()</c>/FK at all — see <see cref="ENROLLMENT"/>'s own doc comment for why a
/// cross-module/cross-schema FK is never added in this module.
/// </para>
/// <para>
/// Three indexes, not two — docs/DATABASE.md's inline "learning" sketch only spells out
/// <c>UQ(UserId, CourseId)</c>/<c>IX(CourseId, Status)</c>, but its separate "Index ที่ต้องมีตั้งแต่วันแรก"
/// (day-one) section also specifies <c>IX_Enrollments_MyCourses (UserId, Status) INCLUDE (CourseId,
/// ProgressPercent, LastAccessedAtUtc)</c> — a different leftmost column (<c>UserId</c>, not
/// <c>CourseId</c>) serving a different real query ("my courses" for a learner dashboard, exactly what
/// <c>EnrollmentService.ListForUserAsync</c> needs) than <c>IX(CourseId, Status)</c> serves ("this course's
/// enrollments", an instructor/admin-facing lookup). Unlike <c>CourseConfiguration</c>'s single covering
/// index (which could fully subsume its module's simpler inline indexes via one leftmost prefix), these
/// three do not overlap enough to collapse into fewer indexes, so all three are kept — same
/// "day-one section is more authoritative than the inline shorthand, but only where it actually
/// covers the same query" reading <c>CourseConfiguration</c>'s own doc comment already establishes.
/// </para>
/// </summary>
public sealed class EnrollmentConfiguration : IEntityTypeConfiguration<ENROLLMENT>
{
    public void Configure(EntityTypeBuilder<ENROLLMENT> builder)
    {
        builder.ToTable("ENROLLMENTS", "LEARNING");

        builder.HasKey(x => x.ENROLLMENT_ID);
        builder.Property(x => x.ENROLLMENT_ID).HasColumnName("ENROLLMENT_ID");

        builder.Property(x => x.USER_ID).HasColumnName("USER_ID").IsRequired();
        builder.Property(x => x.COURSE_ID).HasColumnName("COURSE_ID").IsRequired();
        builder.Property(x => x.ORDER_ID).HasColumnName("ORDER_ID");

        builder.Property(x => x.SOURCE).HasColumnName("SOURCE").HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(x => x.ENROLLED_AT_UTC).HasColumnName("ENROLLED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(x => x.EXPIRES_AT_UTC).HasColumnName("EXPIRES_AT_UTC").HasPrecision(3);

        builder.Property(x => x.STATUS).HasColumnName("STATUS").HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(x => x.PROGRESS_PERCENT).HasColumnName("PROGRESS_PERCENT").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.COMPLETED_AT_UTC).HasColumnName("COMPLETED_AT_UTC").HasPrecision(3);
        builder.Property(x => x.LAST_ACCESSED_AT_UTC).HasColumnName("LAST_ACCESSED_AT_UTC").HasPrecision(3);

        // First concurrency token in this module — an enrollment's progress can be written from multiple
        // devices/tabs in quick succession, same reasoning Course.RowVersion's own doc comment gives.
        builder.Property(x => x.ROW_VERSION).HasColumnName("ROW_VERSION").IsRowVersion();

        // A learner can never hold two rows for the same course — see this class's own doc comment.
        builder.HasIndex(x => new { x.USER_ID, x.COURSE_ID }).IsUnique().HasDatabaseName("IX_ENROLLMENTS_USER_ID_COURSE_ID");

        // Inline sketch's IX(CourseId, Status) — "this course's enrollments by status" (instructor/admin).
        builder.HasIndex(x => new { x.COURSE_ID, x.STATUS }).HasDatabaseName("IX_ENROLLMENTS_COURSE_ID_STATUS");

        // Day-one IX_Enrollments_MyCourses — "my active/expired/revoked courses" (learner dashboard). See
        // this class's own doc comment for why this is a third index, not a redundant one.
        builder.HasIndex(x => new { x.USER_ID, x.STATUS })
            .IncludeProperties(x => new { x.COURSE_ID, x.PROGRESS_PERCENT, x.LAST_ACCESSED_AT_UTC })
            .HasDatabaseName("IX_ENROLLMENTS_MY_COURSES");

        builder.Property(x => x.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(x => x.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
