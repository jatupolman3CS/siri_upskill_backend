using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Domain;

/// <summary>
/// A learner's access grant to a course — docs/DATABASE.md's "learning" section: "Enrollments(Id PK,
/// UserId FK, CourseId FK, OrderId FK NULL, Source, EnrolledAtUtc, ExpiresAtUtc NULL, Status,
/// ProgressPercent decimal(5,2), CompletedAtUtc, LastAccessedAtUtc, RowVersion) UQ(UserId, CourseId)
/// IX(CourseId, Status)". The aggregate root for this scaffold pass' whole Enrollment/Progress/
/// Certificate cluster (<see cref="EPISODE_PROGRESS"/>/<see cref="WATCH_EVENT"/>/<see cref="CERTIFICATE"/>
/// all reference an enrollment by id, but are NOT child collections here — each is its own small
/// aggregate with its own repository, same "separate small aggregate with its own lifecycle" shape
/// <c>Siri.Modules.Learning.Domain.QUIZ_ATTEMPT</c> already establishes relative to <c>QUIZ</c>).
/// <para>
/// <b>UPPERCASE naming exception (this entity, and every other entity this scaffold pass adds to this
/// module, only):</b> docs/DECISIONS.md D-17 — the 7 brand-new modules (Learning included) use UPPERCASE
/// for entity class/property names and DB table/column names, unlike the rest of this codebase
/// (Identity/Catalog/Notification stay PascalCase, vertical-slice). <see cref="IAuditable"/>'s four
/// properties (<see cref="CreatedAtUtc"/>/<see cref="CreatedBy"/>/<see cref="UpdatedAtUtc"/>/
/// <see cref="UpdatedBy"/>) are the one deliberate exception, kept normal PascalCase:
/// <c>AuditableEntityInterceptor.Apply</c> (confirmed by reading its actual source before writing this)
/// looks them up via <c>entry.Property(nameof(IAuditable.CreatedAtUtc))</c> — a hardcoded C#-member-name
/// string lookup ("CreatedAtUtc") against the entity's real CLR property. Rename that property and the
/// interceptor's lookup silently stops matching, throwing <see cref="InvalidOperationException"/> at
/// <c>SaveChangesAsync</c> for every <see cref="IAuditable"/> entity in the whole solution, not just this
/// one. The four properties' *columns* still map to UPPERCASE (<c>CREATED_AT_UTC</c> etc.) via
/// <c>.HasColumnName(...)</c> in <c>EnrollmentConfiguration</c> — only the C# property name is the
/// exception, not the schema. <see cref="EPISODE_PROGRESS"/>/<see cref="CERTIFICATE"/> follow this same
/// exception without repeating the reasoning in full; <see cref="WATCH_EVENT"/> does not implement
/// <see cref="IAuditable"/> at all (see that class's own doc comment for why).
/// </para>
/// <para>
/// <b>No hard delete, no cascade delete (this entity — and CLAUDE.md's ground rule #5 names it by name):
/// </b> "ห้าม cascade delete หรือ hard delete กับข้อมูลเงินและสิทธิ์เรียน (Orders, Payments, RevenueSplits,
/// Enrollments, Certificates)". There is no delete method anywhere on this class, and never will be — a
/// learner's access is revoked via <see cref="Revoke"/> (sets <see cref="STATUS"/> to
/// <see cref="EnrollmentStatus.Revoked"/>), never removed. Every FK pointing INTO <c>ENROLLMENTS</c> from
/// elsewhere in this module (<see cref="EPISODE_PROGRESS.ENROLLMENT_ID"/>/
/// <see cref="CERTIFICATE.ENROLLMENT_ID"/>/<see cref="WATCH_EVENT.ENROLLMENT_ID"/>) is <c>NoAction</c> at
/// the database level, with one deliberate exception — see <see cref="EPISODE_PROGRESS"/>'s own doc
/// comment for why that one is <c>Cascade</c> instead, and why that does not actually contradict this rule
/// in practice (this table is never hard-deleted by any application code path, so that FK's cascade
/// behavior never fires).
/// </para>
/// <para>
/// <see cref="USER_ID"/> (→ <c>identity.Users</c>), <see cref="COURSE_ID"/> (→ <c>catalog.Courses</c>), and
/// <see cref="ORDER_ID"/> (→ <c>commerce.Orders</c>, not built yet) all carry no database-level FK
/// constraint — a cross-module/cross-schema FK is exactly the physical coupling docs/ARCHITECTURE.md §1
/// keeps modules as separate projects to avoid (same reasoning already established by Catalog's
/// <c>Course.TrailerMediaAssetId</c> and this module's own <c>QUIZ.EPISODE_ID</c>). Existence of the
/// referenced user/course/order is an application-layer concern for whichever later task actually wires
/// real enrollment creation (most likely triggered by an order-paid event from <c>Siri.Modules.Commerce</c>,
/// not a public HTTP call — same "admin endpoint exists for ops/backfill, real trigger is an event" shape
/// <c>Siri.Modules.Payout.Domain.REVENUE_SPLIT</c>'s own doc comment already establishes).
/// </para>
/// <para>
/// Implemented for real as of 2026-08-24 (the old SCAFFOLD note here was stale). Two rules worth keeping
/// in view before editing: the table has a unique index on (USER_ID, COURSE_ID), so re-buying a course that
/// is Expired/Revoked must go through <see cref="Reactivate"/> on the existing row rather than inserting a
/// second one; and progress fields are deliberately preserved on reactivation — that was decided with the
/// project owner, so do not reset them without asking.
/// </para>
/// </summary>
public sealed class ENROLLMENT : IAuditable
{
    /// <summary>EF Core materialization only — never used to build a usable instance from code.</summary>
    private ENROLLMENT()
    {
    }

    public Guid ENROLLMENT_ID { get; private set; }

    /// <summary>FK to <c>identity.Users.Id</c> conceptually — see this class's own doc comment for why
    /// there is no database-level FK constraint.</summary>
    public Guid USER_ID { get; private set; }

    /// <summary>FK to <c>catalog.Courses.Id</c> conceptually — see this class's own doc comment for why
    /// there is no database-level FK constraint.</summary>
    public Guid COURSE_ID { get; private set; }

    /// <summary>FK to <c>commerce.Orders.Id</c> conceptually, <c>null</c> for a non-purchase
    /// <see cref="SOURCE"/> (Gift/Admin/Corporate) — see this class's own doc comment for why there is no
    /// database-level FK constraint. <c>Siri.Modules.Commerce</c> does not exist yet in this codebase.
    /// </summary>
    public Guid? ORDER_ID { get; private set; }

    /// <summary>How this enrollment came to exist — see <see cref="EnrollmentSource"/>'s own doc comment,
    /// notably that <see cref="EnrollmentSource.Corporate"/> is a future P9 value nothing in this codebase
    /// produces yet.</summary>
    public EnrollmentSource SOURCE { get; private set; }

    public DateTime ENROLLED_AT_UTC { get; private set; }

    /// <summary><c>null</c> = lifetime access (mirrors <c>Course.AccessDurationDays</c>'s "null = no
    /// limit" shape). Once past, a later task's scheduled job transitions <see cref="STATUS"/> to
    /// <see cref="EnrollmentStatus.Expired"/> — see that enum's own doc comment.</summary>
    public DateTime? EXPIRES_AT_UTC { get; private set; }

    public EnrollmentStatus STATUS { get; private set; }

    /// <summary><c>decimal(5,2)</c> — a percentage (0–100), not a raw watched-seconds count (that detail
    /// lives per-episode on <see cref="EPISODE_PROGRESS"/>). Denormalized/aggregated across this
    /// enrollment's episodes — a later task decides the exact roll-up rule, same
    /// "<c>CourseStatsUpdater</c>-only, one place writes it" spirit <c>Course.EpisodeCount</c> etc. already
    /// establish in Catalog, even though this column is not in that specific denormalization list.</summary>
    public decimal PROGRESS_PERCENT { get; private set; }

    /// <summary><c>null</c> until <see cref="Complete"/>.</summary>
    public DateTime? COMPLETED_AT_UTC { get; private set; }

    /// <summary><c>null</c> until the first <see cref="UpdateProgress"/> call — this column exists
    /// specifically to answer "which of my courses did I open most recently" for a learner dashboard
    /// (docs/DATABASE.md's day-one <c>IX_Enrollments_MyCourses</c> index, see
    /// <c>EnrollmentConfiguration</c>).</summary>
    public DateTime? LAST_ACCESSED_AT_UTC { get; private set; }

    /// <summary>EF concurrency token (SQL Server <c>rowversion</c>) — database.md calls for one on tables
    /// that get concurrently edited; an enrollment's progress can be written from multiple devices/tabs in
    /// quick succession (same reasoning <c>Course.RowVersion</c>'s own doc comment gives).</summary>
    public byte[] ROW_VERSION { get; private set; } = [];

    // ---- IAuditable (stays PascalCase — see this class's own doc comment) -----------------------
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    /// <summary>
    /// Grants a new <see cref="EnrollmentStatus.Active"/> enrollment.
    /// </summary>
    public static ENROLLMENT Create(Guid userId, Guid courseId, Guid? orderId, EnrollmentSource source, DateTime? expiresAtUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        if (courseId == Guid.Empty)
        {
            throw new ArgumentException("Course ID cannot be empty.", nameof(courseId));
        }

        return new ENROLLMENT
        {
            ENROLLMENT_ID = UuidV7.NewId(),
            USER_ID = userId,
            COURSE_ID = courseId,
            ORDER_ID = orderId,
            SOURCE = source,
            ENROLLED_AT_UTC = clock.UtcNow,
            EXPIRES_AT_UTC = expiresAtUtc,
            STATUS = EnrollmentStatus.Active,
            PROGRESS_PERCENT = 0m,
            CreatedAtUtc = clock.UtcNow,
        };
    }

    /// <summary>
    /// Updates <see cref="PROGRESS_PERCENT"/> and stamps <see cref="LAST_ACCESSED_AT_UTC"/> together.
    /// </summary>
    public void UpdateProgress(decimal progressPercent, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (progressPercent < 0m || progressPercent > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(progressPercent), progressPercent, "Progress percentage must be between 0 and 100.");
        }

        PROGRESS_PERCENT = Math.Max(PROGRESS_PERCENT, progressPercent);
        LAST_ACCESSED_AT_UTC = clock.UtcNow;

        if (PROGRESS_PERCENT >= 100m && COMPLETED_AT_UTC is null)
        {
            Complete(clock);
        }
    }

    /// <summary>Marks the enrollment fully completed.</summary>
    public void Complete(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        COMPLETED_AT_UTC ??= clock.UtcNow;
    }

    /// <summary>Transitions to <see cref="EnrollmentStatus.Expired"/> once <see cref="EXPIRES_AT_UTC"/> has passed.</summary>
    public void Expire()
    {
        STATUS = EnrollmentStatus.Expired;
    }

    /// <summary>Administratively revokes access (refund, policy violation, ...).</summary>
    public void Revoke()
    {
        STATUS = EnrollmentStatus.Revoked;
    }

    /// <summary>
    /// Reactivates an expired or revoked enrollment for a new purchase — deliberately preserves
    /// <see cref="PROGRESS_PERCENT"/>/<see cref="COMPLETED_AT_UTC"/> rather than resetting them: a learner
    /// who lets access lapse and repurchases shouldn't lose prior progress just because the clock ran out
    /// in between. <paramref name="orderId"/> is re-pointed at the order that paid for this reactivation
    /// (the original <see cref="ORDER_ID"/> refers to a purchase that no longer grants access).
    /// </summary>
    public void Reactivate(Guid? orderId, DateTime? expiresAtUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        STATUS = EnrollmentStatus.Active;
        ORDER_ID = orderId;
        EXPIRES_AT_UTC = expiresAtUtc;
        UpdatedAtUtc = clock.UtcNow;
    }
}
