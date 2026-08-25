using Siri.SharedKernel;

namespace Siri.Modules.Learning.Domain;

/// <summary>
/// One raw playback telemetry event from the player — docs/DATABASE.md's "learning" section:
/// "WatchEvents(Id bigint identity PK, EnrollmentId, EpisodeId, EventType, PositionSeconds, OccurredAtUtc)
/// -- volume สูง: partition ตามเดือน + purge &gt; 12 เดือน หลัง rollup แล้ว". Appended by the (not-yet-built)
/// playback-heartbeat flow — see <see cref="WatchEventType"/>'s own doc comment.
/// <para>
/// <b>PK type is this module's one deliberate exception to database.md's "PK = uniqueidentifier ค่า
/// UUIDv7" rule</b> — <see cref="WATCH_EVENT_ID"/> is a <c>bigint</c> identity column, not a
/// <c>uniqueidentifier</c>. This table is a high-volume, append-only log (every play/pause/seek/ended/
/// heartbeat from every learner, every episode) — DATABASE.md's own note above calls out partitioning and
/// scheduled purging, and <c>analytics.DailyCourseStats</c>/<c>analytics.EpisodeDropOff</c> are explicitly
/// built FROM this table by a nightly Hangfire job, never queried realtime. A sequential
/// <c>bigint IDENTITY</c> is the right shape for that access pattern (cheap monotonic ordering for
/// time-windowed batch scans/partitioning, 8 bytes instead of 16, no risk of ever running out at any
/// plausible event volume) in a way UUIDv7 does not add value over — UUIDv7 exists specifically to avoid
/// the random-insert-order index fragmentation problem <c>NEWID()</c>/GUIDv4 causes, but a plain
/// <c>IDENTITY</c> column has the same "sequential, no fragmentation" property already, for less storage
/// and no client-side id-generation step, which matters at this table's expected row volume.
/// </para>
/// <para>
/// Class name is singular (<c>WATCH_EVENT</c>), matching docs/DECISIONS.md D-17's "C# entity class:
/// UPPERCASE เอกพจน์" convention — the table itself is still plural (<c>WATCH_EVENTS</c>, see
/// <c>WatchEventConfiguration</c>), and the bare <c>Id</c> PK is manually prefixed with the singular
/// entity name (<see cref="WATCH_EVENT_ID"/>, not <c>ID</c>) per that same convention.
/// </para>
/// <para>
/// Does NOT implement <see cref="Siri.Persistence.Conventions.IAuditable"/> — an append-only log row is
/// never updated after insert, and <see cref="OCCURRED_AT_UTC"/> already records the one timestamp that
/// matters. See <see cref="ENROLLMENT"/>'s own doc comment for the full UPPERCASE-naming-exception
/// reasoning (not repeated here since there is no <see cref="Siri.Persistence.Conventions.IAuditable"/>
/// property on this entity to carve out).
/// </para>
/// <para>
/// <see cref="ENROLLMENT_ID"/> gets a real (<c>NoAction</c>) FK to <see cref="ENROLLMENT"/> — same
/// module/schema, both sides built in this exact scaffold pass (contrast this module's own
/// <c>QUIZ_ATTEMPT.ENROLLMENT_ID</c>, which has none, because it was scaffolded by a sibling task before
/// <see cref="ENROLLMENT"/> existed — see that class's own doc comment). <c>NoAction</c>, not
/// <c>Cascade</c>: an append-only log's rows should never silently vanish as a side effect of a parent
/// row's lifecycle — DATABASE.md's own comment above says this table is purged independently, by a
/// scheduled rollup+purge job, not by any FK cascade. <see cref="EPISODE_ID"/> is a conceptual FK to
/// <c>catalog.CourseEpisodes.Id</c> with no database-level FK constraint at all, ever — cross-module/
/// cross-schema, same reasoning <c>Course.TrailerMediaAssetId</c> already establishes.
/// </para>
/// <para>
/// This is a SCAFFOLD pass — see <see cref="ENROLLMENT"/>'s own doc comment for what that means for method
/// bodies here. No dedicated Service/Endpoints exist for this entity in this scaffold pass — see
/// <see cref="IWatchEventRepository"/>'s own doc comment for why.
/// </para>
/// </summary>
public sealed class WATCH_EVENT
{
    /// <summary>EF Core materialization only.</summary>
    private WATCH_EVENT()
    {
    }

    /// <summary><c>bigint IDENTITY</c>, not a UUIDv7 <see cref="Guid"/> — see this class's own doc comment
    /// for why. Left at its CLR default (<c>0</c>) by <see cref="Create"/>; the database assigns the real
    /// value on insert.</summary>
    public long WATCH_EVENT_ID { get; private set; }

    /// <summary>FK to <see cref="ENROLLMENT.ENROLLMENT_ID"/> — see this class's own doc comment for why
    /// this is <c>NoAction</c>, not <c>Cascade</c>.</summary>
    public Guid ENROLLMENT_ID { get; private set; }

    /// <summary>FK to <c>catalog.CourseEpisodes.Id</c> conceptually — see this class's own doc comment for
    /// why there is no database-level FK constraint.</summary>
    public Guid EPISODE_ID { get; private set; }

    public WatchEventType EVENT_TYPE { get; private set; }

    /// <summary>Playhead position in seconds at the moment this event fired.</summary>
    public int POSITION_SECONDS { get; private set; }

    public DateTime OCCURRED_AT_UTC { get; private set; }

    /// <summary>
    /// Appends a new telemetry event.
    /// </summary>
    public static WATCH_EVENT Create(Guid enrollmentId, Guid episodeId, WatchEventType eventType, int positionSeconds, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (enrollmentId == Guid.Empty)
        {
            throw new ArgumentException("Enrollment ID cannot be empty.", nameof(enrollmentId));
        }

        if (episodeId == Guid.Empty)
        {
            throw new ArgumentException("Episode ID cannot be empty.", nameof(episodeId));
        }

        return new WATCH_EVENT
        {
            ENROLLMENT_ID = enrollmentId,
            EPISODE_ID = episodeId,
            EVENT_TYPE = eventType,
            POSITION_SECONDS = Math.Max(0, positionSeconds),
            OCCURRED_AT_UTC = clock.UtcNow,
        };
    }
}
