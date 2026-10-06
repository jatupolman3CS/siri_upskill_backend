using Siri.Modules.Catalog.Contracts;

namespace Siri.UnitTests.Catalog;

/// <summary>Unit tests for <see cref="LiveSessionDisplayStateCalculator"/> — task P11-01,
/// docs/contracts/P11-01-catalog-live-sessions.md §2.5/§2.7.</summary>
public class LiveSessionDisplayStateCalculatorTests
{
    private static readonly DateTime Starts = new(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ends = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Compute_StatusCancelled_ReturnsCancelledRegardlessOfTime()
    {
        // Even though "now" is long after EndsAtUtc (would otherwise be Ended), Cancelled wins.
        var result = LiveSessionDisplayStateCalculator.Compute(
            LiveSessionStatus.Cancelled, Starts, Ends, Ends.AddDays(1));

        Assert.Equal(LiveSessionDisplayState.Cancelled, result);
    }

    [Fact]
    public void Compute_NowAtExactlyJoinWindowBoundary_ReturnsLiveInclusive()
    {
        var nowUtc = Starts.AddMinutes(-LiveSessionDisplayStateCalculator.DefaultJoinWindowBeforeMinutes);

        var result = LiveSessionDisplayStateCalculator.Compute(LiveSessionStatus.Scheduled, Starts, Ends, nowUtc);

        Assert.Equal(LiveSessionDisplayState.Live, result);
    }

    [Fact]
    public void Compute_NowOneTickBeforeJoinWindowBoundary_ReturnsUpcoming()
    {
        var nowUtc = Starts.AddMinutes(-LiveSessionDisplayStateCalculator.DefaultJoinWindowBeforeMinutes).AddTicks(-1);

        var result = LiveSessionDisplayStateCalculator.Compute(LiveSessionStatus.Scheduled, Starts, Ends, nowUtc);

        Assert.Equal(LiveSessionDisplayState.Upcoming, result);
    }

    [Fact]
    public void Compute_NowAtExactlyEndsAtUtc_ReturnsLiveNotEnded()
    {
        // The guard is "nowUtc > endsAtUtc" (strictly greater), not ">=" — a session is still
        // considered Live at the exact instant it ends.
        var result = LiveSessionDisplayStateCalculator.Compute(LiveSessionStatus.Scheduled, Starts, Ends, Ends);

        Assert.Equal(LiveSessionDisplayState.Live, result);
    }

    [Fact]
    public void Compute_NowOneTickAfterEndsAtUtc_ReturnsEnded()
    {
        var result = LiveSessionDisplayStateCalculator.Compute(LiveSessionStatus.Scheduled, Starts, Ends, Ends.AddTicks(1));

        Assert.Equal(LiveSessionDisplayState.Ended, result);
    }

    [Fact]
    public void Compute_LongBeforeStart_ReturnsUpcoming()
    {
        var result = LiveSessionDisplayStateCalculator.Compute(LiveSessionStatus.Scheduled, Starts, Ends, Starts.AddDays(-1));

        Assert.Equal(LiveSessionDisplayState.Upcoming, result);
    }

    [Fact]
    public void Compute_BetweenStartAndEnd_ReturnsLive()
    {
        var nowUtc = Starts.AddHours(1);

        var result = LiveSessionDisplayStateCalculator.Compute(LiveSessionStatus.Scheduled, Starts, Ends, nowUtc);

        Assert.Equal(LiveSessionDisplayState.Live, result);
    }

    [Fact]
    public void Compute_CustomJoinWindow_IsNotHardcodedToFifteenMinutes()
    {
        // A 60-minute join window means "now" 20 minutes before start is already inside the window
        // (would be Upcoming under the default 15-minute window), proving joinWindowBeforeMinutes is a
        // real parameter, not a hardcoded constant baked into the comparison.
        var nowUtc = Starts.AddMinutes(-20);

        var result = LiveSessionDisplayStateCalculator.Compute(
            LiveSessionStatus.Scheduled, Starts, Ends, nowUtc, joinWindowBeforeMinutes: 60);

        Assert.Equal(LiveSessionDisplayState.Live, result);
    }
}
