using Siri.SharedKernel;

namespace Siri.Modules.Learning.Domain;

/// <summary>
/// One learner's watch progress for a single episode within one <see cref="ENROLLMENT"/> —
/// docs/DATABASE.md's "learning" section: "EpisodeProgress(Id PK, EnrollmentId FK, EpisodeId FK,
/// LastPositionSeconds, WatchedSeconds, IsCompleted bit, CompletedAtUtc, UpdatedAtUtc) UQ(EnrollmentId,
/// EpisodeId)". A true composition child of <see cref="ENROLLMENT"/> at the database level (its
/// <see cref="ENROLLMENT_ID"/> FK cascades — see this class's own doc comment below), but NOT a navigation
/// collection on that aggregate (no <c>Enrollment.EpisodeProgresses</c> property, no
/// <c>Enrollment.AddEpisodeProgress()</c> method) — it is its own small aggregate with its own repository,
/// because in practice it is written by a playback-heartbeat call keyed on (enrollmentId, episodeId), not
/// through any <see cref="ENROLLMENT"/>-aggregate operation. This is deliberately simpler than
/// <see cref="ENROLLMENT"/>'s own doc comment describes for itself: this scaffold's
/// <c>EpisodeProgressService</c> exposes only <c>UpsertProgressAsync</c>/<c>GetForEnrollmentAsync</c> — no
/// separate create/delete actions (see that service's own doc comment).
/// <para>
/// Table name is deliberately NOT pluralized to <c>EPISODE_PROGRESSES</c> despite docs/DECISIONS.md D-17's
/// "table เป็นพหูพจน์เสมอ" convention — "progress" is an uncountable noun in English (no natural plural),
/// and docs/DATABASE.md's own sketch already spells this table <c>EpisodeProgress</c>, not
/// <c>EpisodeProgresses</c>. The D-17 pluralization rule exists specifically to dodge T-SQL reserved words
/// (<c>ORDER</c>/<c>USER</c>/<c>GROUP</c> → <c>ORDERS</c>/<c>USERS</c>/<c>GROUPS</c>); <c>PROGRESS</c> is
/// not a reserved word, so that purpose does not apply here either. Table and class share the exact same
/// name (<c>EPISODE_PROGRESS</c>) as a result — see <c>EpisodeProgressConfiguration</c>.
/// </para>
/// <para>
/// <b>UPPERCASE naming exception</b> — see <see cref="ENROLLMENT"/>'s own doc comment for the full
/// reasoning. Unlike <see cref="ENROLLMENT"/>/<see cref="CERTIFICATE"/>, this entity does NOT implement
/// <see cref="Siri.Persistence.Conventions.IAuditable"/> — it already carries its own
/// <see cref="UPDATED_AT_UTC"/> (a heartbeat-style "last write" stamp, not a full Created/Updated/By audit
/// trail), so <see cref="UPDATED_AT_UTC"/> is a perfectly ordinary UPPERCASE domain property here, set
/// directly by <see cref="Touch"/> — NOT the <see cref="Siri.Persistence.Conventions.IAuditable"/>-backed,
/// interceptor-stamped, forced-PascalCase property <see cref="ENROLLMENT"/>'s own
/// <c>UpdatedAtUtc</c> is. The PascalCase carve-out only applies to entities that actually implement that
/// interface.
/// </para>
/// <para>
/// <see cref="EPISODE_ID"/> is a conceptual FK to <c>catalog.CourseEpisodes.Id</c> — no database-level FK
/// constraint, ever: cross-module/cross-schema, same reasoning <c>Course.TrailerMediaAssetId</c> and this
/// module's own <c>QUIZ.EPISODE_ID</c> already establish. <see cref="ENROLLMENT_ID"/> is different: same
/// module/schema, and both sides of the relationship are built in this exact scaffold pass, so it gets a
/// real FK — <c>Cascade</c>, the one deliberate exception to this module's usual <c>NoAction</c>/no-FK
/// stance on <see cref="ENROLLMENT"/> references (contrast <see cref="CERTIFICATE"/>'s own doc comment,
/// which stays <c>NoAction</c> despite the same same-module/same-scaffold-pass situation). The difference:
/// this row has no independent meaning without its enrollment (a true composition child, same shape
/// <c>Siri.Modules.Payout.Domain.PAYOUT_BATCH_ITEM.BATCH_ID</c>'s own doc comment already establishes for
/// its own module's one <c>Cascade</c> exception) — a certificate, by contrast, is a legal/verifiable
/// artifact CLAUDE.md's ground rule #5 names explicitly by name, so it deliberately does not cascade even
/// though it would be technically identical to set up. In practice neither cascade path ever fires,
/// because <see cref="ENROLLMENT"/> is never hard-deleted by any application code (ground rule #5) — this
/// is a defensive DB-level guarantee for a row category, not a behavior any endpoint in this scaffold pass
/// relies on.
/// </para>
/// <para>
/// This is a SCAFFOLD pass — see <see cref="ENROLLMENT"/>'s own doc comment for what that means for method
/// bodies here.
/// </para>
/// </summary>
public sealed class EPISODE_PROGRESS
{
    /// <summary>EF Core materialization only.</summary>
    private EPISODE_PROGRESS()
    {
    }

    public Guid EPISODE_PROGRESS_ID { get; private set; }

    /// <summary>FK to <see cref="ENROLLMENT.ENROLLMENT_ID"/> — <c>Cascade</c>, see this class's own doc
    /// comment for why.</summary>
    public Guid ENROLLMENT_ID { get; private set; }

    /// <summary>FK to <c>catalog.CourseEpisodes.Id</c> conceptually — see this class's own doc comment for
    /// why there is no database-level FK constraint.</summary>
    public Guid EPISODE_ID { get; private set; }

    /// <summary>Playhead position in seconds the learner last stopped at — drives "resume from where you
    /// left off" (docs/DATABASE.md's day-one <c>IX_EpisodeProgress_Resume</c> index, see
    /// <c>EpisodeProgressConfiguration</c>).</summary>
    public int LAST_POSITION_SECONDS { get; private set; }

    /// <summary>Cumulative distinct seconds actually watched — NOT the same as
    /// <see cref="LAST_POSITION_SECONDS"/> (a learner who seeks back and rewatches a segment accumulates
    /// more watched time than their raw playhead position would suggest). Exact accumulation rule is a
    /// later task's business logic, not this scaffold's.</summary>
    public int WATCHED_SECONDS { get; private set; }

    public bool IS_COMPLETED { get; private set; }

    /// <summary><c>null</c> until <see cref="IS_COMPLETED"/> first becomes <c>true</c>.</summary>
    public DateTime? COMPLETED_AT_UTC { get; private set; }

    /// <summary>Plain UPPERCASE domain property, NOT an <see cref="Siri.Persistence.Conventions.IAuditable"/>
    /// member — see this class's own doc comment.</summary>
    public DateTime UPDATED_AT_UTC { get; private set; }

    /// <summary>Creates the first progress row for an (enrollment, episode) pair.</summary>
    public static EPISODE_PROGRESS Create(Guid enrollmentId, Guid episodeId, int lastPositionSeconds, int watchedSeconds, bool isCompleted, IClock clock)
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

        return new EPISODE_PROGRESS
        {
            EPISODE_PROGRESS_ID = UuidV7.NewId(),
            ENROLLMENT_ID = enrollmentId,
            EPISODE_ID = episodeId,
            LAST_POSITION_SECONDS = Math.Max(0, lastPositionSeconds),
            WATCHED_SECONDS = Math.Max(0, watchedSeconds),
            IS_COMPLETED = isCompleted,
            COMPLETED_AT_UTC = isCompleted ? clock.UtcNow : null,
            UPDATED_AT_UTC = clock.UtcNow,
        };
    }

    /// <summary>Overwrites this row's watch state from a fresh heartbeat/seek/ended event and stamps UPDATED_AT_UTC.</summary>
    public void Touch(int lastPositionSeconds, int watchedSeconds, bool isCompleted, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        LAST_POSITION_SECONDS = Math.Max(0, lastPositionSeconds);
        WATCHED_SECONDS = Math.Max(WATCHED_SECONDS, watchedSeconds);

        if (!IS_COMPLETED && isCompleted)
        {
            IS_COMPLETED = true;
            COMPLETED_AT_UTC = clock.UtcNow;
        }

        UPDATED_AT_UTC = clock.UtcNow;
    }
}
