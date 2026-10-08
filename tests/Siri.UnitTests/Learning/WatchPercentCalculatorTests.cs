using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure.Contracts;
using Xunit;

namespace Siri.UnitTests.Learning;

/// <summary>
/// The drop-off rollup's AvgWatchPercent used to fall back to a hardcoded 50% whenever a viewer had a
/// non-zero position but no "ended" event. These tests pin the replacement: real positions over the real
/// episode duration, and nothing at all when it cannot be measured.
/// </summary>
public sealed class WatchPercentCalculatorTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void AveragePercent_HeartbeatOnlyViewerWithKnownDuration_IsPositionOverDurationNotAFixedFifty()
    {
        var samples = new[]
        {
            new WatchSample(A, WatchEventType.Heartbeat, 30),
            new WatchSample(A, WatchEventType.Heartbeat, 90),
        };

        // Furthest position 90 of a 360s episode = 25%.
        Assert.Equal(25m, WatchPercentCalculator.AveragePercent(samples, 360));
    }

    [Fact]
    public void AveragePercent_ViewerWhoEnded_CountsAsHundredEvenWithoutADuration()
    {
        var samples = new[] { new WatchSample(A, WatchEventType.Ended, 300) };

        Assert.Equal(100m, WatchPercentCalculator.AveragePercent(samples, null));
    }

    [Fact]
    public void AveragePercent_MixedViewers_AveragesPerViewerNotPerEvent()
    {
        var samples = new[]
        {
            // A: many heartbeats, furthest 100 of 400 = 25%
            new WatchSample(A, WatchEventType.Heartbeat, 10),
            new WatchSample(A, WatchEventType.Heartbeat, 50),
            new WatchSample(A, WatchEventType.Heartbeat, 100),
            // B: finished = 100%
            new WatchSample(B, WatchEventType.Ended, 400),
        };

        Assert.Equal(62.5m, WatchPercentCalculator.AveragePercent(samples, 400));
    }

    [Fact]
    public void AveragePercent_PositionPastTheEnd_IsCappedAtHundred()
    {
        var samples = new[] { new WatchSample(A, WatchEventType.Heartbeat, 5_000) };

        Assert.Equal(100m, WatchPercentCalculator.AveragePercent(samples, 300));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void AveragePercent_NoDurationAndNobodyEnded_IsZeroNotAGuess(int? duration)
    {
        var samples = new[] { new WatchSample(A, WatchEventType.Heartbeat, 120) };

        Assert.Equal(0m, WatchPercentCalculator.AveragePercent(samples, duration));
    }

    [Fact]
    public void AveragePercent_UnmeasurableViewersAreLeftOutOfTheAverage()
    {
        // Duration unknown: A (heartbeat only) is unmeasurable and skipped; B ended = 100%.
        var samples = new[]
        {
            new WatchSample(A, WatchEventType.Heartbeat, 120),
            new WatchSample(B, WatchEventType.Ended, 300),
        };

        Assert.Equal(100m, WatchPercentCalculator.AveragePercent(samples, null));
    }

    [Fact]
    public void AveragePercent_NoSamples_IsZero()
    {
        Assert.Equal(0m, WatchPercentCalculator.AveragePercent([], 300));
    }

    [Fact]
    public void AveragePercent_NegativePositions_AreTreatedAsZeroNotNegativePercent()
    {
        var samples = new[] { new WatchSample(A, WatchEventType.Heartbeat, -20) };

        Assert.Equal(0m, WatchPercentCalculator.AveragePercent(samples, 300));
    }
}
