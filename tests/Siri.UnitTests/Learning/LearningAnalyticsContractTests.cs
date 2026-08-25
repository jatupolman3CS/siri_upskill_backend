using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class LearningAnalyticsContractTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    [Fact]
    public void WatchEvent_Create_SetsPropertiesCorrectly()
    {
        var now = new DateTime(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var enrollmentId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        var evt = WATCH_EVENT.Create(enrollmentId, episodeId, WatchEventType.Play, 120, clock);

        Assert.Equal(enrollmentId, evt.ENROLLMENT_ID);
        Assert.Equal(episodeId, evt.EPISODE_ID);
        Assert.Equal(WatchEventType.Play, evt.EVENT_TYPE);
        Assert.Equal(120, evt.POSITION_SECONDS);
        Assert.Equal(now, evt.OCCURRED_AT_UTC);
    }

    [Fact]
    public void EpisodeDropOffItem_StoresValuesCorrectly()
    {
        var episodeId = Guid.NewGuid();
        var item = new EpisodeDropOffItem(episodeId, 100, 85, 85.0m);

        Assert.Equal(episodeId, item.EpisodeId);
        Assert.Equal(100, item.StartCount);
        Assert.Equal(85, item.CompleteCount);
        Assert.Equal(85.0m, item.AvgWatchPercent);
    }

    [Fact]
    public void DailyCourseActivityItem_StoresValuesCorrectly()
    {
        var courseId = Guid.NewGuid();
        var item = new DailyCourseActivityItem(courseId, 450, 20, 75.0m);

        Assert.Equal(courseId, item.CourseId);
        Assert.Equal(450, item.Views);
        Assert.Equal(20, item.Enrollments);
        Assert.Equal(75.0m, item.CompletionRate);
    }
}
