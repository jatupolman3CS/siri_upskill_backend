namespace Siri.Modules.Analytics.Domain;

/// <summary>
/// One calendar day's rollup of view/enrollment/revenue/completion activity for a single course — see
/// docs/DATABASE.md's "analytics" section: <c>analytics.DailyCourseStats(Date, CourseId, Views,
/// Enrollments, Revenue, CompletionRate) PK(Date,CourseId)</c>.
/// <para>
/// <b>UPPERCASE_WITH_UNDERSCORES naming</b> (this class, every property, and the mapped table/columns —
/// see <c>DailyCourseStatConfiguration</c>) is a deliberate, scoped exception to this repo's normal
/// PascalCase convention (.claude/rules/database.md): docs/DECISIONS.md's D-17 calls out 7 brand-new
/// modules (Analytics is one) to use this naming plus Repository+Service instead of vertical-slice —
/// Identity/Catalog/Notification keep PascalCase untouched. Repository/interface/DTO types around this
/// entity (<c>DailyCourseStatRepository</c>, <c>IDailyCourseStatRepository</c>,
/// <c>AppDbContextAnalyticsExtensions</c>) are deliberately <b>not</b> part of that exception and stay
/// ordinary PascalCase, per D-17.
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>/<see cref="Siri.Persistence.Conventions.ISoftDelete"/>,
/// and no <c>{Entity}Service</c>/endpoints anywhere in this module: every row here is a nightly
/// Hangfire-job upsert built from <c>learning.WatchEvents</c> (docs/DATABASE.md: "ทั้งสองตารางสร้างจาก
/// WatchEvents ด้วย Hangfire job รายคืน (ไม่ query realtime)") — a mechanically-overwritten rollup row, not
/// an entity with its own creation/update audit trail, a delete lifecycle, or a directly-callable REST
/// resource of its own. A later task (instructor analytics API, P4-04) will read these rows filtered by
/// <c>catalog.Courses.InstructorId</c> — that is that future task's read-query logic to build, not
/// something this scaffold should guess at.
/// </para>
/// <para>
/// <see cref="COURSE_ID"/> has no FK — <c>catalog.Courses</c> is a different module/schema
/// (<c>Siri.Modules.Catalog</c>), and a database-level FK spanning two modules' schemas is exactly the
/// physical coupling docs/ARCHITECTURE.md §1 keeps modules as separate projects to avoid — same reasoning
/// <c>Course.TrailerMediaAssetId</c> already established for its own cross-module reference.
/// </para>
/// </summary>
public sealed class DAILY_COURSE_STAT
{
    /// <summary>EF Core materialization only.</summary>
    private DAILY_COURSE_STAT()
    {
    }

    public DateOnly DATE { get; private set; }

    /// <summary>No FK — see this class's own doc comment.</summary>
    public Guid COURSE_ID { get; private set; }

    public int VIEWS { get; private set; }

    public int ENROLLMENTS { get; private set; }

    public decimal REVENUE { get; private set; }

    /// <summary>A percentage in the 0.00–100.00 range (not a 0–1 fraction) — see
    /// <c>DailyCourseStatConfiguration</c> for the <c>decimal(5,2)</c> precision this assumption drives.
    /// The nightly rollup job (a later task) owns the actual definition of "completion"; this scaffold
    /// only fixes the storage shape.</summary>
    public decimal COMPLETION_RATE { get; private set; }

    /// <summary>Creates the row shell for a given day/course — every metric starts at its zero default
    /// until <see cref="ApplyRollup"/> fills them in. No aggregate-lifecycle meaning beyond that: unlike
    /// e.g. <c>Course.Create</c>, this isn't the start of a domain lifecycle, just the identity half of an
    /// upsert the nightly job performs (find via <c>IDailyCourseStatRepository</c>, or <see cref="Create"/>
    /// if missing, then always <see cref="ApplyRollup"/>).</summary>
    public static DAILY_COURSE_STAT Create(DateOnly date, Guid courseId) =>
        new()
        {
            DATE = date,
            COURSE_ID = courseId,
        };

    public void ApplyRollup(int views, int enrollments, decimal revenue, decimal completionRate)
    {
        VIEWS = views;
        ENROLLMENTS = enrollments;
        REVENUE = revenue;
        COMPLETION_RATE = completionRate;
    }
}
