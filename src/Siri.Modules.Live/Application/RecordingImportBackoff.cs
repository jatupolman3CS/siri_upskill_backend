namespace Siri.Modules.Live.Application;

/// <summary>
/// How long the recording import waits between looks (P11-13 contract section 6): <b>10 minutes, 20 minutes, 40 minutes, then hourly</b>. Pure functions, so the
/// schedule is unit-tested without a clock.
/// <para>
/// Two uses, one schedule: <see cref="ForFailure"/> spaces the retries after a transient failure (attempt 1 → 10 min, 2 → 20 min, ...), and
/// <see cref="ForSearch"/> spaces the polls while Google has no finished recording yet. A poll is not a failure and must not eat the failure budget, so the
/// poll count is not stored: it is derived from how long ago the first search was due (the polls fall at +0, +10, +30, +70, +130, +190 minutes ...).
/// </para>
/// </summary>
public static class RecordingImportBackoff
{
    /// <summary>The delay after the <paramref name="step"/>-th look (0-based): 10, 20, 40, then 60 minutes.</summary>
    public static TimeSpan ForStep(int step) => step switch
    {
        <= 0 => TimeSpan.FromMinutes(10),
        1 => TimeSpan.FromMinutes(20),
        2 => TimeSpan.FromMinutes(40),
        _ => TimeSpan.FromMinutes(60),
    };

    /// <summary>The delay before the retry that follows the <paramref name="attemptsSoFar"/>-th consecutive transient failure (1 → 10 min, 2 → 20 min, 3 → 40 min, then hourly).</summary>
    public static TimeSpan ForFailure(int attemptsSoFar) => ForStep(attemptsSoFar - 1);

    /// <summary>The delay before the next poll of an empty search, given how long ago the first search was due (<paramref name="sinceFirstSearch"/>).</summary>
    public static TimeSpan ForSearch(TimeSpan sinceFirstSearch)
    {
        if (sinceFirstSearch < TimeSpan.Zero)
        {
            sinceFirstSearch = TimeSpan.Zero;
        }

        // The polls so far fell at cumulative marks 0, 10, 30, 70, 130, ...: the next delay is the one of the first mark still ahead of "now".
        var mark = TimeSpan.Zero;
        for (var step = 0; ; step++)
        {
            mark += ForStep(step);
            if (mark > sinceFirstSearch || step >= 3)
            {
                return ForStep(step);
            }
        }
    }
}
