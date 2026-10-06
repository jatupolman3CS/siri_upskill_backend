using Siri.Modules.Catalog.Domain;

namespace Siri.UnitTests.Catalog;

/// <summary>Unit tests for <see cref="COURSE"/>'s live-session domain methods (task P11-01,
/// docs/contracts/P11-01-catalog-live-sessions.md §2.3/§2.7) — <see cref="COURSE.AddLiveSession"/>/
/// <see cref="COURSE.UpdateLiveSession"/>/<see cref="COURSE.CancelLiveSession"/>/
/// <see cref="COURSE.AttachSessionRecording"/>/<see cref="COURSE.SetDeliveryFormat"/>. Kept in its own
/// file rather than appended to <c>CourseTests.cs</c> — a large, self-contained new invariant surface,
/// same "new file when the addition is substantial" judgment call the contract left open.</summary>
public class CourseLiveSessionTests
{
    private static readonly DateTime Now = new(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc);

    private static COURSE CreateDraftCourse() =>
        COURSE.Create("live-course", "Live COURSE", Guid.NewGuid(), Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 990m);

    private static COURSE CreateLiveCourse()
    {
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(DeliveryFormat.Live);
        return course;
    }

    // ---- AddLiveSession -----------------------------------------------------------------------

    [Theory]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void AddLiveSession_OnLiveOrHybridCourse_Succeeds(DeliveryFormat format)
    {
        var clock = new FakeClock(Now);
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(format);
        var start = clock.UtcNow.AddDays(1);

        var session = course.AddLiveSession("Kickoff เซสชั่นแรก", "แนะนำคอร์ส + Q&A", start, start.AddHours(2), clock);

        Assert.Equal(CourseLiveSessionStatus.Scheduled, session.Status);
        Assert.Equal(0, session.SortOrder);
        Assert.Equal(start, session.StartsAtUtc);
        Assert.Equal(start.AddHours(2), session.EndsAtUtc);
        Assert.Null(session.CancelReason);
        Assert.Null(session.RecordingEpisodeId);
        Assert.Single(course.LiveSessions);
    }

    [Fact]
    public void AddLiveSession_OnDemandCourse_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateDraftCourse(); // defaults to OnDemand

        Assert.Throws<InvalidOperationException>(() =>
            course.AddLiveSession("S", null, clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(1), clock));
        Assert.Empty(course.LiveSessions);
    }

    [Fact]
    public void AddLiveSession_ArchivedCourse_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        course.Archive();

        Assert.Throws<InvalidOperationException>(() =>
            course.AddLiveSession("S", null, clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(1), clock));
    }

    [Fact]
    public void AddLiveSession_DurationBelowMinimum_ThrowsArgumentException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);

        Assert.Throws<ArgumentException>(() => course.AddLiveSession("S", null, start, start.AddMinutes(14), clock));
    }

    [Fact]
    public void AddLiveSession_DurationExactlyFifteenMinutes_Succeeds()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);

        var session = course.AddLiveSession("S", null, start, start.AddMinutes(15), clock);

        Assert.Equal(start.AddMinutes(15), session.EndsAtUtc);
    }

    [Fact]
    public void AddLiveSession_DurationExactlyEightHours_Succeeds()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);

        var session = course.AddLiveSession("S", null, start, start.AddHours(8), clock);

        Assert.Equal(start.AddHours(8), session.EndsAtUtc);
    }

    [Fact]
    public void AddLiveSession_DurationAboveMaximum_ThrowsArgumentException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);

        Assert.Throws<ArgumentException>(() => course.AddLiveSession("S", null, start, start.AddHours(8).AddSeconds(1), clock));
    }

    [Fact]
    public void AddLiveSession_OverlapsAnotherScheduledSession_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        course.AddLiveSession("First", null, start, start.AddHours(2), clock);

        Assert.Throws<InvalidOperationException>(() =>
            course.AddLiveSession("Second", null, start.AddHours(1), start.AddHours(3), clock));
    }

    [Fact]
    public void AddLiveSession_OverlapsCancelledSession_Succeeds()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var first = course.AddLiveSession("First", null, start, start.AddHours(2), clock);
        course.CancelLiveSession(first.Id, "เปลี่ยนแผน", clock);

        var second = course.AddLiveSession("Second", null, start, start.AddHours(2), clock);

        Assert.Equal(2, course.LiveSessions.Count);
        Assert.Equal(CourseLiveSessionStatus.Scheduled, second.Status);
    }

    [Fact]
    public void AddLiveSession_BackToBackWithAnotherSession_DoesNotOverlap()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var first = course.AddLiveSession("First", null, start, start.AddHours(1), clock);

        var second = course.AddLiveSession("Second", null, first.EndsAtUtc, first.EndsAtUtc.AddHours(1), clock);

        Assert.Equal(2, course.LiveSessions.Count);
        Assert.Equal(first.EndsAtUtc, second.StartsAtUtc);
    }

    [Fact]
    public void AddLiveSession_StartsInThePast_ThrowsArgumentOutOfRangeException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            course.AddLiveSession("S", null, clock.UtcNow.AddMinutes(-1), clock.UtcNow.AddHours(1), clock));
    }

    [Fact]
    public void AddLiveSession_NonUtcDateTimeKind_ThrowsArgumentException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var localStart = DateTime.SpecifyKind(clock.UtcNow.AddDays(1), DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => course.AddLiveSession("S", null, localStart, localStart.AddHours(1), clock));
    }

    [Fact]
    public void AddLiveSession_MultipleSessions_AutoAppendsSortOrder()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);

        var first = course.AddLiveSession("First", null, start, start.AddHours(1), clock);
        var second = course.AddLiveSession("Second", null, start.AddDays(1), start.AddDays(1).AddHours(1), clock);

        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);
        Assert.Equal(2, course.LiveSessions.Count);
    }

    // ---- UpdateLiveSession ----------------------------------------------------------------------

    [Fact]
    public void UpdateLiveSession_ValidReschedule_UpdatesFields()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("Title", "Desc", start, start.AddHours(1), clock);

        var newStart = start.AddDays(2);
        course.UpdateLiveSession(session.Id, "New Title", "New Desc", newStart, newStart.AddHours(2), clock);

        Assert.Equal("New Title", session.Title);
        Assert.Equal("New Desc", session.Description);
        Assert.Equal(newStart, session.StartsAtUtc);
        Assert.Equal(newStart.AddHours(2), session.EndsAtUtc);
    }

    [Fact]
    public void UpdateLiveSession_SessionCurrentlyInProgress_CanExtendEndTime()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = Now.AddHours(1);
        var end = start.AddHours(1);
        var session = course.AddLiveSession("Live now", null, start, end, clock);

        // Move "now" into the session's window — StartsAtUtc has passed but EndsAtUtc has not.
        clock.UtcNow = start.AddMinutes(30);

        var newEnd = end.AddMinutes(30);
        course.UpdateLiveSession(session.Id, "Live now", null, start, newEnd, clock);

        Assert.Equal(newEnd, session.EndsAtUtc);
    }

    [Fact]
    public void UpdateLiveSession_SessionAlreadyEnded_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = Now.AddHours(1);
        var end = start.AddHours(1);
        var session = course.AddLiveSession("S", null, start, end, clock);

        clock.UtcNow = end.AddMinutes(1);

        Assert.Throws<InvalidOperationException>(() =>
            course.UpdateLiveSession(session.Id, "S", null, start, end.AddHours(1), clock));
    }

    [Fact]
    public void UpdateLiveSession_SessionCancelled_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);
        course.CancelLiveSession(session.Id, null, clock);

        Assert.Throws<InvalidOperationException>(() =>
            course.UpdateLiveSession(session.Id, "S", null, start, start.AddHours(2), clock));
    }

    [Fact]
    public void UpdateLiveSession_NewWindowOverlapsAnotherSession_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        course.AddLiveSession("First", null, start, start.AddHours(1), clock);
        var second = course.AddLiveSession("Second", null, start.AddDays(1), start.AddDays(1).AddHours(1), clock);

        Assert.Throws<InvalidOperationException>(() =>
            course.UpdateLiveSession(second.Id, "Second", null, start, start.AddHours(1), clock));
    }

    [Fact]
    public void UpdateLiveSession_KeepsSameWindow_ExcludesItselfFromOverlapCheck()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);

        // Updating with the exact same window must not throw due to "overlapping itself".
        course.UpdateLiveSession(session.Id, "S renamed", null, start, start.AddHours(1), clock);

        Assert.Equal("S renamed", session.Title);
    }

    [Fact]
    public void UpdateLiveSession_UnknownSessionId_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();

        Assert.Throws<InvalidOperationException>(() =>
            course.UpdateLiveSession(Guid.NewGuid(), "S", null, clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(1), clock));
    }

    // ---- CancelLiveSession ----------------------------------------------------------------------

    [Fact]
    public void CancelLiveSession_WithReason_SetsCancelledStatusAndReason()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);

        course.CancelLiveSession(session.Id, "ผู้สอนติดธุระเร่งด่วน", clock);

        Assert.Equal(CourseLiveSessionStatus.Cancelled, session.Status);
        Assert.Equal("ผู้สอนติดธุระเร่งด่วน", session.CancelReason);
    }

    [Fact]
    public void CancelLiveSession_WithoutReason_SetsCancelledStatusAndNullReason()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);

        course.CancelLiveSession(session.Id, null, clock);

        Assert.Equal(CourseLiveSessionStatus.Cancelled, session.Status);
        Assert.Null(session.CancelReason);
    }

    [Fact]
    public void CancelLiveSession_BlankReason_IsNormalizedToNull()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);

        course.CancelLiveSession(session.Id, "   ", clock);

        Assert.Null(session.CancelReason);
    }

    [Fact]
    public void CancelLiveSession_SessionAlreadyEnded_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = Now.AddHours(1);
        var end = start.AddHours(1);
        var session = course.AddLiveSession("S", null, start, end, clock);

        clock.UtcNow = end.AddMinutes(1);

        Assert.Throws<InvalidOperationException>(() => course.CancelLiveSession(session.Id, null, clock));
    }

    [Fact]
    public void CancelLiveSession_AlreadyCancelled_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);
        course.CancelLiveSession(session.Id, null, clock);

        Assert.Throws<InvalidOperationException>(() => course.CancelLiveSession(session.Id, "อีกครั้ง", clock));
    }

    // ---- AttachSessionRecording -----------------------------------------------------------------

    [Fact]
    public void AttachSessionRecording_ValidEpisode_SetsRecordingEpisodeId()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);
        var section = course.AddSection("Recordings");
        var episode = course.AddEpisode(section.Id, "Recorded session", null, false);

        course.AttachSessionRecording(session.Id, episode.Id);

        Assert.Equal(episode.Id, session.RecordingEpisodeId);
    }

    [Fact]
    public void AttachSessionRecording_CancelledSession_StillSucceeds()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);
        course.CancelLiveSession(session.Id, null, clock);
        var section = course.AddSection("Recordings");
        var episode = course.AddEpisode(section.Id, "Recorded session", null, false);

        course.AttachSessionRecording(session.Id, episode.Id);

        Assert.Equal(episode.Id, session.RecordingEpisodeId);
    }

    [Fact]
    public void AttachSessionRecording_EpisodeNotOnThisCourse_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);

        Assert.Throws<InvalidOperationException>(() => course.AttachSessionRecording(session.Id, Guid.NewGuid()));
        Assert.Null(session.RecordingEpisodeId);
    }

    [Fact]
    public void AttachSessionRecording_CalledTwice_OverwritesPreviousValueWithoutThrowing()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);
        var section = course.AddSection("Recordings");
        var firstEpisode = course.AddEpisode(section.Id, "First upload", null, false);
        var secondEpisode = course.AddEpisode(section.Id, "Correct upload", null, false);

        course.AttachSessionRecording(session.Id, firstEpisode.Id);
        course.AttachSessionRecording(session.Id, secondEpisode.Id);

        Assert.Equal(secondEpisode.Id, session.RecordingEpisodeId);
    }

    // ---- SetDeliveryFormat ----------------------------------------------------------------------

    [Theory]
    [InlineData(DeliveryFormat.OnDemand, DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.OnDemand, DeliveryFormat.Hybrid)]
    [InlineData(DeliveryFormat.Live, DeliveryFormat.Hybrid)]
    [InlineData(DeliveryFormat.Hybrid, DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Live, DeliveryFormat.OnDemand)]
    [InlineData(DeliveryFormat.Hybrid, DeliveryFormat.OnDemand)]
    public void SetDeliveryFormat_NoBlockingScheduledSessions_Succeeds(DeliveryFormat from, DeliveryFormat to)
    {
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(from);

        course.SetDeliveryFormat(to);

        Assert.Equal(to, course.DeliveryFormat);
    }

    [Fact]
    public void SetDeliveryFormat_ToOnDemandWithScheduledSessions_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        course.AddLiveSession("S", null, start, start.AddHours(1), clock);

        Assert.Throws<InvalidOperationException>(() => course.SetDeliveryFormat(DeliveryFormat.OnDemand));
        Assert.Equal(DeliveryFormat.Live, course.DeliveryFormat);
    }

    [Fact]
    public void SetDeliveryFormat_ToOnDemandWithOnlyCancelledSessions_Succeeds()
    {
        var clock = new FakeClock(Now);
        var course = CreateLiveCourse();
        var start = clock.UtcNow.AddDays(1);
        var session = course.AddLiveSession("S", null, start, start.AddHours(1), clock);
        course.CancelLiveSession(session.Id, null, clock);

        course.SetDeliveryFormat(DeliveryFormat.OnDemand);

        Assert.Equal(DeliveryFormat.OnDemand, course.DeliveryFormat);
    }

    [Fact]
    public void SetDeliveryFormat_ArchivedCourse_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        course.Archive();

        Assert.Throws<InvalidOperationException>(() => course.SetDeliveryFormat(DeliveryFormat.Live));
    }
}
