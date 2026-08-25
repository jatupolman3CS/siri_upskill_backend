using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Community.Domain;

/// <summary>
/// A discussion post in a course's Q&amp;A tab (docs/DATABASE.md's "community" section — backs the
/// classroom mockup's Q&amp;A tab, docs/DECISIONS.md D-17). Threaded via self-referencing
/// <see cref="PARENT_ID"/>: <c>null</c> = a top-level post, non-null = a reply to another
/// <see cref="DISCUSSION"/>.
/// <para>
/// <b>UPPERCASE naming (D-17):</b> Community is one of 7 brand-new modules (Commerce/Media/Learning/
/// Payout/Cms/Community/Analytics) that use UPPERCASE entity class/property names and DB table/column
/// names instead of this repo's established PascalCase (Identity/Catalog/Notification — untouched, still
/// vertical-slice/PascalCase) — see docs/DECISIONS.md D-17 and .claude/rules/backend.md /
/// .claude/rules/database.md for the full reasoning.
/// </para>
/// <para>
/// <b>The one documented exception</b>: <see cref="IsDeleted"/>, <see cref="DeletedAtUtc"/>,
/// <see cref="CreatedAtUtc"/>, <see cref="CreatedBy"/>, <see cref="UpdatedAtUtc"/>, and
/// <see cref="UpdatedBy"/> below stay PascalCase in C# even on this otherwise-UPPERCASE entity, because
/// <c>Siri.Persistence.Interceptors.AuditableEntityInterceptor</c> looks each of them up via
/// <c>nameof(IAuditable.CreatedAtUtc)</c> etc. — a hardcoded C#-member-name string lookup against EF's
/// change-tracker property metadata (<c>entry.Property(name).CurrentValue = ...</c>), shared by every
/// entity in the whole database, not something this module owns or can override per-entity. Renaming any
/// of these six C# properties would make that lookup throw <see cref="InvalidOperationException"/> at
/// runtime on every single <c>SaveChangesAsync</c> call, for every <see cref="IAuditable"/>/
/// <see cref="ISoftDelete"/> entity in the system — not just this one. Only the *columns* these six map
/// to are UPPERCASE (see <see cref="Infrastructure.DiscussionConfiguration"/>'s explicit
/// <c>.HasColumnName(...)</c> calls) — the C# property name itself is the one place UPPERCASE does not
/// apply.
/// </para>
/// <para>
/// <b>Why this entity implements <see cref="ISoftDelete"/>:</b> docs/DATABASE.md's "กติกาทั่วไป" table
/// names the soft-delete list explicitly: "<c>IsDeleted bit</c> + <c>DeletedAtUtc</c> เฉพาะตารางที่ต้อง
/// กู้คืนได้ (Course, Post, Discussion)" — this entity is that "Discussion" entry. A learner's or
/// instructor's Q&amp;A post must be recoverable (a moderation mistake, an accidental self-delete someone
/// wants undone), not permanently gone the instant it's removed — the same reasoning that already keeps
/// <c>Siri.Modules.Catalog.Domain.Course</c> soft-deletable rather than hard-deleted. Deleting a
/// <see cref="DISCUSSION"/> never issues a SQL <c>DELETE</c>: <c>AuditableEntityInterceptor</c> coerces a
/// <c>DbSet.Remove(...)</c> call into an <see cref="IsDeleted"/> flag flip automatically, and
/// <c>Siri.Persistence.Conventions.ModelBuilderExtensions.ApplySoftDeleteQueryFilter</c> (wired once,
/// centrally, in <c>AppDbContext.OnModelCreating</c>) hides soft-deleted rows from every normal query —
/// this module does not need to write either mechanism itself, only implement the interface correctly and
/// configure the column names (<see cref="Infrastructure.DiscussionConfiguration"/>).
/// </para>
/// <para>
/// No FK to <c>catalog.Courses</c>/<c>catalog.CourseEpisodes</c> (<see cref="COURSE_ID"/>/
/// <see cref="EPISODE_ID"/>) or <c>identity.Users</c> (<see cref="USER_ID"/>) — same cross-module/
/// cross-schema reasoning already documented on <c>Siri.Modules.Catalog.Domain.Course
/// .TrailerMediaAssetId</c>: a database-level FK spanning two modules' schemas is exactly the physical
/// coupling docs/ARCHITECTURE.md §1 keeps modules as separate projects to avoid (independent service
/// extraction later). <see cref="PARENT_ID"/> is different — a same-table self-reference, so it does get
/// a real FK (<see cref="Infrastructure.DiscussionConfiguration"/>, <c>Restrict</c>: SQL Server rejects
/// <c>Cascade</c> on a self-referencing FK outright, same reasoning already documented on
/// <c>Siri.Modules.Catalog.Domain.Category</c>'s self-referencing <c>ParentId</c>).
/// </para>
/// <para>
/// <b>Scaffold pass only</b> (docs/DECISIONS.md D-17's handoff) — <see cref="Create"/> is a stub; no other
/// mutator exists yet on purpose. See <see cref="Application.DiscussionService"/>'s own doc comment for
/// why <see cref="UPVOTE_COUNT"/>/<see cref="STATUS"/> transitions aren't added speculatively ahead of the
/// follow-up task that actually implements them.
/// </para>
/// </summary>
public sealed class DISCUSSION : IAuditable, ISoftDelete
{
    /// <summary>EF Core materialization only.</summary>
    private DISCUSSION()
    {
    }

    public Guid DISCUSSION_ID { get; private set; }

    /// <summary>No FK — see this class's own doc comment.</summary>
    public Guid COURSE_ID { get; private set; }

    /// <summary><c>null</c> = a course-level post, not tied to one specific lesson. No FK — see this
    /// class's own doc comment.</summary>
    public Guid? EPISODE_ID { get; private set; }

    /// <summary>The post's author. No FK — see this class's own doc comment.</summary>
    public Guid USER_ID { get; private set; }

    /// <summary><c>null</c> = a top-level post; set = a reply to another <see cref="DISCUSSION"/>.
    /// Self-referencing FK, <c>Restrict</c> — see this class's own doc comment.</summary>
    public Guid? PARENT_ID { get; private set; }

    public string BODY { get; private set; } = string.Empty;

    /// <summary>Whether <see cref="USER_ID"/> was the course's instructor at the moment this was posted —
    /// decided by the caller (<see cref="Application.DiscussionService"/>), not this entity: knowing who
    /// teaches <see cref="COURSE_ID"/> is Catalog's data, out of reach from here (same cross-module
    /// boundary as the missing FKs documented above).</summary>
    public bool IS_INSTRUCTOR_ANSWER { get; private set; }

    /// <summary>Moderation state — distinct from <see cref="IsDeleted"/>: a <see cref="DiscussionStatus.Hidden"/>
    /// post is still a live row an admin can restore, while <see cref="IsDeleted"/> is the author's own
    /// (recoverable, per this class's own doc comment) removal.</summary>
    public DiscussionStatus STATUS { get; private set; }

    public int UPVOTE_COUNT { get; private set; }

    // ---- ISoftDelete (stays PascalCase — see this class's own doc comment) -------------------------
    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    bool ISoftDelete.IsDeleted
    {
        get => IsDeleted;
        set => IsDeleted = value;
    }

    DateTime? ISoftDelete.DeletedAtUtc
    {
        get => DeletedAtUtc;
        set => DeletedAtUtc = value;
    }

    // ---- IAuditable (stays PascalCase — see this class's own doc comment) --------------------------
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

    public static DISCUSSION Create(Guid courseId, Guid? episodeId, Guid userId, Guid? parentId, string body, bool isInstructorAnswer = false)
    {
        if (courseId == Guid.Empty) throw new ArgumentException("Course ID cannot be empty.", nameof(courseId));
        if (userId == Guid.Empty) throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new DISCUSSION
        {
            DISCUSSION_ID = UuidV7.NewId(),
            COURSE_ID = courseId,
            EPISODE_ID = episodeId,
            USER_ID = userId,
            PARENT_ID = parentId,
            BODY = body.Trim(),
            IS_INSTRUCTOR_ANSWER = isInstructorAnswer,
            STATUS = DiscussionStatus.Visible,
            UPVOTE_COUNT = 0,
            IsDeleted = false,
        };
    }

    public void Upvote() => UPVOTE_COUNT++;

    public void Hide() => STATUS = DiscussionStatus.Hidden;

    public void Restore() => STATUS = DiscussionStatus.Visible;
}
