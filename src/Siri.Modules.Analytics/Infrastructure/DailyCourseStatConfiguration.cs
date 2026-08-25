using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Analytics.Domain;

namespace Siri.Modules.Analytics.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="DAILY_COURSE_STAT"/> — see docs/DATABASE.md's "analytics" section and
/// that entity's own doc comment for the module-wide UPPERCASE naming exception (docs/DECISIONS.md D-17).
/// </summary>
public sealed class DailyCourseStatConfiguration : IEntityTypeConfiguration<DAILY_COURSE_STAT>
{
    public void Configure(EntityTypeBuilder<DAILY_COURSE_STAT> builder)
    {
        builder.ToTable("DAILY_COURSE_STATS", "ANALYTICS");

        // Composite PK, no synthetic Id column at all (docs/DATABASE.md's sketch: "PK(Date,CourseId)") —
        // one row per course per rollup day, always upserted by the nightly job (a later task), never
        // created through a request-scoped handler.
        builder.HasKey(x => new { x.DATE, x.COURSE_ID });

        builder.Property(x => x.VIEWS).IsRequired();
        builder.Property(x => x.ENROLLMENTS).IsRequired();
        // decimal(18,2) — database.md's blanket money convention, same precision Course.Price uses.
        builder.Property(x => x.REVENUE).HasPrecision(18, 2).IsRequired();
        // decimal(5,2) — a 0.00-100.00 percentage (see DAILY_COURSE_STAT.COMPLETION_RATE's own doc
        // comment for that assumption): ample headroom over the valid range without the wasted width
        // decimal(18,2) would carry for a value that never needs money-sized precision.
        builder.Property(x => x.COMPLETION_RATE).HasPrecision(5, 2).IsRequired();

        // IDailyCourseStatRepository.GetForCourseAsync queries by (COURSE_ID, DATE range) — the composite
        // PK above is keyed (DATE, COURSE_ID), so COURSE_ID alone is not a leftmost-prefix match and that
        // query could not use it. database.md: "เขียน query ใหม่ที่แตะตาราง ... ต้องบอกได้ว่าใช้ index ตัว
        // ไหน ถ้าไม่มีให้เพิ่ม index มาใน migration เดียวกัน".
        builder.HasIndex(x => new { x.COURSE_ID, x.DATE });
    }
}
