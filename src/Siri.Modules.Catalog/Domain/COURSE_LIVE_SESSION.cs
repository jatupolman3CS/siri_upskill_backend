using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// One scheduled live-teaching period inside a <see cref="COURSE"/> whose <see cref="COURSE.DeliveryFormat"/>
/// is <see cref="DeliveryFormat.Live"/> or <see cref="DeliveryFormat.Hybrid"/> — child aggregate of
/// <see cref="COURSE"/>, same composition shape as <see cref="COURSE_SECTION"/>: construction is
/// <c>internal</c>, reachable only through <see cref="COURSE.AddLiveSession"/> (a session without a
/// course owner has no meaning), and every mutator here is <c>internal</c> too, reachable only through
/// the matching method on <see cref="COURSE"/> (<see cref="COURSE.UpdateLiveSession"/>/
/// <see cref="COURSE.CancelLiveSession"/>/<see cref="COURSE.AttachSessionRecording"/>) — <see cref="COURSE"/>
/// is where every cross-session invariant (overlap, duration bounds, "not in the past") actually lives,
/// this entity only holds the state.
/// <para>
/// No <see cref="Siri.Persistence.Conventions.ISoftDelete"/> — deliberately, same reasoning
/// <see cref="COURSE_SECTION"/> is not soft-deleted: a session's lifecycle is Scheduled→Cancelled through
/// <see cref="Status"/>, not a soft-delete flag, and docs/DATABASE.md's soft-delete table list does not
/// name this table. Cancelling never removes the row (see contract docs/contracts/P11-01-catalog-live-sessions.md
/// §3.4's "DELETE vs POST .../cancel" note — both map to the same <see cref="Cancel"/> call).
/// </para>
/// </summary>
public sealed class COURSE_LIVE_SESSION : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private COURSE_LIVE_SESSION()
    {
    }

    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateTime StartsAtUtc { get; private set; }

    public DateTime EndsAtUtc { get; private set; }

    /// <summary>Display order among sibling sessions within the same <see cref="CourseId"/>. Computed by
    /// <see cref="COURSE.AddLiveSession"/> from its own current session count, same "aggregate computes
    /// its own append position" shape as <see cref="COURSE_SECTION.AddEpisode"/> — but used only as a
    /// tiebreaker here, never the primary display order (see <see cref="COURSE.AddLiveSession"/>'s own
    /// doc comment for why: <see cref="StartsAtUtc"/> is what every read model should sort by).</summary>
    public int SortOrder { get; private set; }

    public CourseLiveSessionStatus Status { get; private set; } = CourseLiveSessionStatus.Scheduled;

    public string? CancelReason { get; private set; }

    /// <summary>FK to <c>COURSE_EPISODES.Id</c> conceptually — the catch-up recording once one is
    /// attached (task P11-06). <c>null</c> until then. See <c>CourseLiveSessionConfiguration</c> for why
    /// this edge is <c>NoAction</c>, not <c>Cascade</c>, unlike <see cref="CourseId"/>.</summary>
    public Guid? RecordingEpisodeId { get; private set; }

    /// <summary>EF concurrency token (PostgreSQL <c>bytea</c>, rotated by
    /// <c>Siri.Persistence.Interceptors.ConcurrencyTokenInterceptor</c>) — same shape as
    /// <see cref="COURSE.RowVersion"/>, first use of it below the <see cref="COURSE"/> aggregate root
    /// itself. Sessions get concurrently edited the same way courses do (an instructor rescheduling one
    /// session while another browser tab is mid-edit on the same course), so the same protection applies.</summary>
    public byte[] RowVersion { get; private set; } = [];

    // ---- IAuditable ---------------------------------------------------------------------------
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

    /// <summary>Callable only from <see cref="COURSE.AddLiveSession"/>, which has already run every
    /// invariant (window validity, overlap, future-only) this entity itself does not re-check.</summary>
    internal static COURSE_LIVE_SESSION Create(Guid courseId, string title, string? description, DateTime startsAtUtc, DateTime endsAtUtc, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new COURSE_LIVE_SESSION
        {
            Id = UuidV7.NewId(),
            CourseId = courseId,
            Title = title,
            Description = description,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            SortOrder = sortOrder,
            Status = CourseLiveSessionStatus.Scheduled,
        };
    }

    /// <summary>Callable only from <see cref="COURSE.UpdateLiveSession"/>, which has already validated
    /// the new window (overlap, duration bounds) and the from-state (Scheduled, not yet ended) — no
    /// independent validation here, same "domain method guards the aggregate-level invariant, the child
    /// entity just holds the resulting state" split <see cref="COURSE_SECTION.Reorder"/> already uses.</summary>
    internal void Reschedule(string title, string? description, DateTime startsAtUtc, DateTime endsAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
        Description = description;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }

    /// <summary>Callable only from <see cref="COURSE.CancelLiveSession"/>, which has already validated the
    /// from-state. Empty/whitespace-only <paramref name="reason"/> is normalized to <c>null</c> — the
    /// DELETE endpoint (task P11-02) does not require one.</summary>
    internal void Cancel(string? reason)
    {
        Status = CourseLiveSessionStatus.Cancelled;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>Callable only from <see cref="COURSE.AttachSessionRecording"/>, which has already verified
    /// <paramref name="episodeId"/> belongs to this course. Overwrites any existing value — re-attaching
    /// is a supported correction path (task P11-06), not an error.</summary>
    internal void AttachRecording(Guid episodeId)
    {
        RecordingEpisodeId = episodeId;
    }
}
