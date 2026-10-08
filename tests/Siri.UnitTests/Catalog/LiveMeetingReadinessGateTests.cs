using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features;
using Siri.Modules.Catalog.Infrastructure;
using Siri.SharedKernel;

namespace Siri.UnitTests.Catalog;

/// <summary>The Live/Hybrid publish gate shared by SubmitCourseForReview and ApproveCourse (P11-03 contract section 4.3): every FUTURE, SCHEDULED
/// session needs a usable room; OnDemand courses never reach the reader; past/cancelled sessions are ignored; the failure carries
/// <c>live.meetings_not_ready</c> with the offending session ids.</summary>
public class LiveMeetingReadinessGateTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(Now);

    private static COURSE NewCourse(DeliveryFormat format)
    {
        var course = COURSE.Create("live-gate", "Live Gate", Guid.NewGuid(), Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        if (format != DeliveryFormat.OnDemand)
        {
            course.SetDeliveryFormat(format);
        }

        return course;
    }

    private COURSE_LIVE_SESSION AddFuture(COURSE course, int daysAhead) =>
        course.AddLiveSession($"S{daysAhead}", null, Now.AddDays(daysAhead), Now.AddDays(daysAhead).AddHours(1), _clock);

    [Theory]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public async Task Validate_FutureSessionWithoutARoom_FailsWithTheReasonAndTheSessionIds(DeliveryFormat format)
    {
        var course = NewCourse(format);
        var ready = AddFuture(course, 1);
        var missing1 = AddFuture(course, 3);
        var missing2 = AddFuture(course, 2);
        var reader = new FakeReader(missing1.Id, missing2.Id);

        var result = await LiveMeetingReadinessGate.ValidateAsync(course, reader, _clock, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code); // → HTTP 400
        Assert.Equal("live.meetings_not_ready", result.Error.Reason);
        Assert.Contains("2", result.Error.Message);

        var ids = Assert.IsType<Guid[]>(result.Error.Extensions!["sessionIds"]);
        // Reported in schedule order (earliest first), and only the sessions that are really missing a room.
        Assert.Equal([missing2.Id, missing1.Id], ids);
        Assert.DoesNotContain(ready.Id, ids);

        // The reader was asked about every future scheduled session, once.
        Assert.Single(reader.Asked);
        Assert.Equal(3, reader.Asked[0].Count);
    }

    [Fact]
    public async Task Validate_EveryFutureSessionHasARoom_Passes()
    {
        var course = NewCourse(DeliveryFormat.Live);
        AddFuture(course, 1);
        AddFuture(course, 2);

        var result = await LiveMeetingReadinessGate.ValidateAsync(course, new FakeReader(), _clock, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Validate_OnDemandCourse_NeverCallsTheReader()
    {
        var course = NewCourse(DeliveryFormat.OnDemand);
        var reader = new FakeReader(Guid.NewGuid());

        var result = await LiveMeetingReadinessGate.ValidateAsync(course, reader, _clock, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(reader.Asked);
    }

    [Fact]
    public async Task Validate_LiveCourseWithNoSessionsAtAll_PassesWithoutAskingTheReader()
    {
        var course = NewCourse(DeliveryFormat.Hybrid);
        var reader = new FakeReader(Guid.NewGuid());

        var result = await LiveMeetingReadinessGate.ValidateAsync(course, reader, _clock, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(reader.Asked);
    }

    [Fact]
    public async Task Validate_PastAndCancelledSessions_AreNotCounted()
    {
        var course = NewCourse(DeliveryFormat.Live);
        var future = AddFuture(course, 2);
        var cancelled = AddFuture(course, 3);
        course.CancelLiveSession(cancelled.Id, "ยกเลิก", _clock);
        var past = AddFuture(course, 1);
        _clock.UtcNow = Now.AddDays(1).AddHours(2); // the "past" session (day 1) has now ended; day 2 and 3 are still ahead
        var reader = new FakeReader(past.Id, cancelled.Id, future.Id);

        var result = await LiveMeetingReadinessGate.ValidateAsync(course, reader, _clock, CancellationToken.None);

        Assert.True(result.IsFailure);
        var ids = Assert.IsType<Guid[]>(result.Error.Extensions!["sessionIds"]);
        Assert.Equal([future.Id], ids); // only the still-upcoming scheduled one is judged
        Assert.Equal([future.Id], reader.Asked[0].ToArray());
    }

    [Fact]
    public async Task Validate_OnlyPastSessions_PassesWithoutAskingTheReader()
    {
        var course = NewCourse(DeliveryFormat.Live);
        AddFuture(course, 1);
        _clock.UtcNow = Now.AddDays(5);
        var reader = new FakeReader(Guid.NewGuid());

        var result = await LiveMeetingReadinessGate.ValidateAsync(course, reader, _clock, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(reader.Asked);
    }

    [Fact]
    public async Task NullReader_ReportsNothingMissing_SoHostsWithoutLiveAreNotGated()
    {
        var course = NewCourse(DeliveryFormat.Live);
        AddFuture(course, 1);

        var result = await LiveMeetingReadinessGate.ValidateAsync(course, new NullLiveMeetingReadinessReader(), _clock, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ErrorExtensions_DoNotCarryAnythingButSessionIds()
    {
        var course = NewCourse(DeliveryFormat.Live);
        var session = AddFuture(course, 1);

        var result = await LiveMeetingReadinessGate.ValidateAsync(course, new FakeReader(session.Id), _clock, CancellationToken.None);

        Assert.Equal(["sessionIds"], result.Error.Extensions!.Keys.ToArray());
    }

    /// <summary>Says which session ids lack a usable room, and remembers what it was asked.</summary>
    private sealed class FakeReader(params Guid[] withoutRoom) : ILiveMeetingReadinessReader
    {
        public List<IReadOnlyCollection<Guid>> Asked { get; } = [];

        public Task<IReadOnlyCollection<Guid>> GetSessionsWithoutUsableMeetingAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
        {
            Asked.Add(sessionIds);
            return Task.FromResult<IReadOnlyCollection<Guid>>(sessionIds.Where(withoutRoom.Contains).ToArray());
        }
    }
}
