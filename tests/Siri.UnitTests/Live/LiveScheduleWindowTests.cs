using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;

namespace Siri.UnitTests.Live;

/// <summary><see cref="LiveScheduleWindow"/>: a reader that caps one answer must never make the invite job silently lose the tail.</summary>
public class LiveScheduleWindowTests
{
    private static readonly DateTime Start = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Behaves like Catalog's reader: oldest first, never more than <see cref="LiveScheduleLimits.MaxWindowItems"/> rows.</summary>
    private sealed class CappedSchedule(IReadOnlyList<LiveSessionContext> all) : ILiveScheduleReader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsInWindowAsync(
            DateTime fromUtc, DateTime toUtc, bool includeCancelled, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<LiveSessionContext>>(all
                .Where(c => c.StartsAtUtc >= fromUtc && c.StartsAtUtc < toUtc && (includeCancelled || c.Status == LiveSessionStatus.Scheduled))
                .OrderBy(c => c.StartsAtUtc)
                .Take(LiveScheduleLimits.MaxWindowItems)
                .ToList());
        }

        public Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static List<LiveSessionContext> Sessions(int count, TimeSpan step) =>
        Enumerable.Range(0, count)
            .Select(i => LiveTestData.Context(startsAtUtc: Start + (step * i), endsAtUtc: Start + (step * i) + TimeSpan.FromHours(1)))
            .ToList();

    [Fact]
    public async Task GetAllAsync_AnAnswerUnderTheCap_IsReturnedWithASingleQuery()
    {
        var reader = new CappedSchedule(Sessions(10, TimeSpan.FromHours(1)));

        var result = await LiveScheduleWindow.GetAllAsync(reader, Start, Start.AddDays(30), includeCancelled: true, CancellationToken.None);

        Assert.Equal(10, result.Count);
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task GetAllAsync_MoreSessionsThanOneAnswerHolds_SplitsTheWindow_AndLosesNone()
    {
        var all = Sessions((LiveScheduleLimits.MaxWindowItems * 2) + 500, TimeSpan.FromMinutes(30)); // 2,500 sessions
        var reader = new CappedSchedule(all);

        var result = await LiveScheduleWindow.GetAllAsync(reader, Start, Start.AddDays(180), includeCancelled: true, CancellationToken.None);

        Assert.Equal(all.Count, result.Count);
        Assert.Equal(all.Select(c => c.SessionId).Order(), result.Select(c => c.SessionId).Order());
        Assert.Equal(all.Count, result.Select(c => c.SessionId).Distinct().Count()); // and nothing twice
        Assert.True(reader.Calls > 1);
    }

    [Fact]
    public async Task GetAllAsync_AnEmptyOrInvertedWindow_ReturnsNothing_WithoutQuerying()
    {
        var reader = new CappedSchedule(Sessions(5, TimeSpan.FromHours(1)));

        Assert.Empty(await LiveScheduleWindow.GetAllAsync(reader, Start, Start, true, CancellationToken.None));
        Assert.Empty(await LiveScheduleWindow.GetAllAsync(reader, Start.AddDays(1), Start, true, CancellationToken.None));
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task GetAllAsync_PassesTheCancelledFlagThrough()
    {
        var sessions = Sessions(4, TimeSpan.FromHours(1));
        sessions[1] = sessions[1] with { Status = LiveSessionStatus.Cancelled };
        var reader = new CappedSchedule(sessions);

        var withCancelled = await LiveScheduleWindow.GetAllAsync(reader, Start, Start.AddDays(1), includeCancelled: true, CancellationToken.None);
        var scheduledOnly = await LiveScheduleWindow.GetAllAsync(reader, Start, Start.AddDays(1), includeCancelled: false, CancellationToken.None);

        Assert.Equal(4, withCancelled.Count);
        Assert.Equal(3, scheduledOnly.Count);
    }
}
