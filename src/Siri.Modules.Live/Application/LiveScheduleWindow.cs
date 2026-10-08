using Siri.Modules.Catalog.Contracts;

namespace Siri.Modules.Live.Application;

/// <summary>
/// Reads <b>every</b> session of a start window from <see cref="ILiveScheduleReader"/>. The reader caps one query at
/// <see cref="LiveScheduleLimits.MaxWindowItems"/> rows, so a long window (the invite job looks 180 days ahead) over a busy platform
/// could silently lose its tail — sessions that then never get an invite. This helper detects a saturated answer and splits the
/// window in half until each part fits, so nothing is dropped.
/// </summary>
public static class LiveScheduleWindow
{
    /// <summary>Recursion guard: 2^24 halvings is far finer than one minute over any realistic window.</summary>
    private const int MaxDepth = 24;

    private static readonly TimeSpan SmallestWindow = TimeSpan.FromMinutes(1);

    /// <summary>All sessions whose start falls in <c>[fromUtc, toUtc)</c>, oldest first.</summary>
    public static async Task<IReadOnlyList<LiveSessionContext>> GetAllAsync(
        ILiveScheduleReader schedule,
        DateTime fromUtc,
        DateTime toUtc,
        bool includeCancelled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (toUtc <= fromUtc)
        {
            return [];
        }

        var result = new List<LiveSessionContext>();
        await CollectAsync(schedule, fromUtc, toUtc, includeCancelled, 0, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task CollectAsync(
        ILiveScheduleReader schedule,
        DateTime fromUtc,
        DateTime toUtc,
        bool includeCancelled,
        int depth,
        List<LiveSessionContext> into,
        CancellationToken cancellationToken)
    {
        var page = await schedule.GetSessionContextsInWindowAsync(fromUtc, toUtc, includeCancelled, cancellationToken).ConfigureAwait(false);

        if (page.Count < LiveScheduleLimits.MaxWindowItems || depth >= MaxDepth || toUtc - fromUtc <= SmallestWindow)
        {
            into.AddRange(page);
            return;
        }

        var middle = fromUtc + ((toUtc - fromUtc) / 2);
        await CollectAsync(schedule, fromUtc, middle, includeCancelled, depth + 1, into, cancellationToken).ConfigureAwait(false);
        await CollectAsync(schedule, middle, toUtc, includeCancelled, depth + 1, into, cancellationToken).ConfigureAwait(false);
    }
}
