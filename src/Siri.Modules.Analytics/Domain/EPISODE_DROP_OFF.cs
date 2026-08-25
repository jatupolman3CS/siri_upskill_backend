namespace Siri.Modules.Analytics.Domain;

/// <summary>
/// One calendar day's rollup of start/completion/watch-through activity for a single episode — see
/// docs/DATABASE.md's "analytics" section: <c>analytics.EpisodeDropOff(Date, EpisodeId, StartCount,
/// CompleteCount, AvgWatchPercent) PK(Date,EpisodeId)</c>.
/// <para>
/// See <see cref="DAILY_COURSE_STAT"/>'s own doc comment for the full reasoning behind this module's
/// UPPERCASE_WITH_UNDERSCORES naming exception (docs/DECISIONS.md D-17) and the "no IAuditable/
/// ISoftDelete/Service/Endpoints" scope decision — both apply here identically, not repeated per class.
/// </para>
/// <para>
/// <see cref="EPISODE_ID"/> has no FK — same cross-module/schema reasoning as
/// <see cref="DAILY_COURSE_STAT.COURSE_ID"/> (targets <c>catalog.CourseEpisodes</c>, a different
/// module/schema).
/// </para>
/// </summary>
public sealed class EPISODE_DROP_OFF
{
    /// <summary>EF Core materialization only.</summary>
    private EPISODE_DROP_OFF()
    {
    }

    public DateOnly DATE { get; private set; }

    /// <summary>No FK — see this class's own doc comment.</summary>
    public Guid EPISODE_ID { get; private set; }

    public int START_COUNT { get; private set; }

    public int COMPLETE_COUNT { get; private set; }

    /// <summary>A percentage in the 0.00–100.00 range (not a 0–1 fraction) — same storage-shape assumption
    /// as <see cref="DAILY_COURSE_STAT.COMPLETION_RATE"/>; see <c>EpisodeDropOffConfiguration</c> for the
    /// <c>decimal(5,2)</c> precision it drives.</summary>
    public decimal AVG_WATCH_PERCENT { get; private set; }

    /// <summary>Creates the row shell for a given day/episode — see <see cref="DAILY_COURSE_STAT.Create"/>'s
    /// doc comment; the same upsert-identity-only reasoning applies here.</summary>
    public static EPISODE_DROP_OFF Create(DateOnly date, Guid episodeId) =>
        new()
        {
            DATE = date,
            EPISODE_ID = episodeId,
        };

    public void ApplyRollup(int startCount, int completeCount, decimal avgWatchPercent)
    {
        START_COUNT = startCount;
        COMPLETE_COUNT = completeCount;
        AVG_WATCH_PERCENT = avgWatchPercent;
    }
}
