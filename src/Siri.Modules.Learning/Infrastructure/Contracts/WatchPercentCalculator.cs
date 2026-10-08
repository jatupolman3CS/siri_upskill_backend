using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure.Contracts;

/// <summary>One raw playback event, reduced to what the watch-percentage maths needs.</summary>
internal readonly record struct WatchSample(Guid EnrollmentId, WatchEventType EventType, int PositionSeconds);

/// <summary>
/// Pure maths for "how much of an episode did its viewers watch" — real data only. Each viewer
/// (enrollment) counts once: 100% if they reached the end (an <see cref="WatchEventType.Ended"/> event),
/// otherwise the furthest playhead position they reported divided by the episode's real duration (capped at
/// 100%). A viewer whose percentage cannot be measured — no <see cref="WatchEventType.Ended"/> and no known
/// duration — is left out of the average rather than being guessed at.
/// </summary>
internal static class WatchPercentCalculator
{
    /// <summary>
    /// Average watch percentage (0–100, two decimals) over the viewers whose value is measurable;
    /// <c>0</c> when no viewer is measurable (the caller's start/complete counts are still real).
    /// </summary>
    public static decimal AveragePercent(IEnumerable<WatchSample> samples, int? durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var percents = new List<decimal>();
        foreach (var viewer in samples.GroupBy(s => s.EnrollmentId))
        {
            if (viewer.Any(s => s.EventType == WatchEventType.Ended))
            {
                percents.Add(100m);
                continue;
            }

            if (durationSeconds is > 0)
            {
                var furthest = Math.Max(0, viewer.Max(s => s.PositionSeconds));
                percents.Add(Math.Min(100m, (decimal)furthest * 100m / durationSeconds.Value));
            }
        }

        return percents.Count == 0 ? 0m : Math.Round(percents.Average(), 2, MidpointRounding.AwayFromZero);
    }
}
