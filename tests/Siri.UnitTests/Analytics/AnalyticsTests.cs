using Siri.Modules.Analytics.Domain;
using Xunit;

namespace Siri.UnitTests.Analytics;

public sealed class AnalyticsTests
{
    [Fact]
    public void DailyCourseStat_Create_ApplyRollup()
    {
        var date = new DateOnly(2026, 8, 21);
        var courseId = Guid.NewGuid();

        var stat = DAILY_COURSE_STAT.Create(date, courseId);
        Assert.Equal(date, stat.DATE);
        Assert.Equal(courseId, stat.COURSE_ID);
        Assert.Equal(0, stat.VIEWS);
        Assert.Equal(0, stat.ENROLLMENTS);
        Assert.Equal(0m, stat.REVENUE);
        Assert.Equal(0m, stat.COMPLETION_RATE);

        stat.ApplyRollup(150, 12, 11880m, 68.5m);
        Assert.Equal(150, stat.VIEWS);
        Assert.Equal(12, stat.ENROLLMENTS);
        Assert.Equal(11880m, stat.REVENUE);
        Assert.Equal(68.5m, stat.COMPLETION_RATE);
    }

    [Fact]
    public void EpisodeDropOff_Create_ApplyRollup()
    {
        var date = new DateOnly(2026, 8, 21);
        var episodeId = Guid.NewGuid();

        var dropOff = EPISODE_DROP_OFF.Create(date, episodeId);
        Assert.Equal(date, dropOff.DATE);
        Assert.Equal(episodeId, dropOff.EPISODE_ID);
        Assert.Equal(0, dropOff.START_COUNT);
        Assert.Equal(0, dropOff.COMPLETE_COUNT);
        Assert.Equal(0m, dropOff.AVG_WATCH_PERCENT);

        dropOff.ApplyRollup(85, 70, 92.4m);
        Assert.Equal(85, dropOff.START_COUNT);
        Assert.Equal(70, dropOff.COMPLETE_COUNT);
        Assert.Equal(92.4m, dropOff.AVG_WATCH_PERCENT);
    }
}
