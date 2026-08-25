using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Community.Domain;

/// <summary>
/// A moderation report against a <see cref="DISCUSSION"/> post — "report a discussion post, admin
/// resolves later" (docs/DECISIONS.md D-17 scaffold task). Same UPPERCASE-naming convention and the same
/// documented <see cref="IAuditable"/> exception as <see cref="DISCUSSION"/> — see that class's own doc
/// comment for the full <c>nameof()</c>-lookup reasoning behind why <see cref="CreatedAtUtc"/>/
/// <see cref="CreatedBy"/>/<see cref="UpdatedAtUtc"/>/<see cref="UpdatedBy"/> below stay PascalCase in C#.
/// <para>
/// <b>Not <see cref="ISoftDelete"/></b> — unlike <see cref="DISCUSSION"/>, <c>REPORT</c> is not in
/// docs/DATABASE.md's soft-delete list ("เฉพาะตารางที่ต้องกู้คืนได้ (Course, Post, Discussion)"). A
/// moderation record has no "undo delete" use case, and this scaffold pass adds no delete endpoint for it
/// at all. It still implements <see cref="IAuditable"/> though — every main table gets the universal
/// Created/Updated audit columns per database.md's "กติกาทั่วไป" table regardless of soft-delete status,
/// matching e.g. <c>Siri.Modules.Catalog.Domain.CourseOutcome</c>'s identical IAuditable-only precedent.
/// </para>
/// <para>
/// <see cref="DISCUSSION_ID"/> gets a real FK (<c>NoAction</c> — see
/// <see cref="Infrastructure.ReportConfiguration"/>): same schema/module as <see cref="DISCUSSION"/>, so
/// unlike the cross-module ids this scaffold leaves FK-less elsewhere, there is no reason not to enforce
/// referential integrity here. <see cref="REPORTED_BY_USER_ID"/> has no FK — cross-module to
/// <c>identity.Users</c>, same reasoning as <see cref="DISCUSSION.USER_ID"/>.
/// </para>
/// <para>
/// <b>Scaffold pass only</b> — <see cref="Create"/> is a stub. No <c>Resolve</c>/<c>Dismiss</c> mutator
/// exists yet on purpose: see <see cref="Application.ReportService"/>'s own doc comment for why those are
/// left for the follow-up task that actually implements them, rather than added speculatively now.
/// </para>
/// </summary>
public sealed class REPORT : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private REPORT()
    {
    }

    public Guid REPORT_ID { get; private set; }

    /// <summary>FK to <see cref="DISCUSSION.DISCUSSION_ID"/> — see this class's own doc comment.</summary>
    public Guid DISCUSSION_ID { get; private set; }

    /// <summary>The reporter. No FK — see this class's own doc comment.</summary>
    public Guid REPORTED_BY_USER_ID { get; private set; }

    public string REASON { get; private set; } = string.Empty;

    public ReportStatus STATUS { get; private set; }

    /// <summary><c>null</c> until an admin acts (<c>Resolve</c>/<c>Dismiss</c> — not yet implemented, see
    /// this class's own doc comment).</summary>
    public DateTime? RESOLVED_AT_UTC { get; private set; }

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

    public static REPORT Create(Guid discussionId, Guid reportedByUserId, string reason)
    {
        if (discussionId == Guid.Empty) throw new ArgumentException("Discussion ID cannot be empty.", nameof(discussionId));
        if (reportedByUserId == Guid.Empty) throw new ArgumentException("Reported by user ID cannot be empty.", nameof(reportedByUserId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new REPORT
        {
            REPORT_ID = UuidV7.NewId(),
            DISCUSSION_ID = discussionId,
            REPORTED_BY_USER_ID = reportedByUserId,
            REASON = reason.Trim(),
            STATUS = ReportStatus.Pending,
        };
    }

    public void Resolve(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        STATUS = ReportStatus.Resolved;
        RESOLVED_AT_UTC = clock.UtcNow;
    }

    public void Dismiss(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        STATUS = ReportStatus.Dismissed;
        RESOLVED_AT_UTC = clock.UtcNow;
    }
}
