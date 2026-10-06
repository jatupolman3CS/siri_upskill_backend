using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Features.CreateAnnouncement;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Notification;

public sealed class AnnouncementTests
{
    [Fact]
    public void Create_WhenValid_SetsPropertiesCorrectly()
    {
        var now = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var courseId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();

        var announcement = ANNOUNCEMENT.Create(
            courseId,
            instructorId,
            "Welcome to COURSE",
            "This is the announcement body",
            sendEmail: true,
            scheduledAtUtc: null,
            clock);

        Assert.NotEqual(Guid.Empty, announcement.Id);
        Assert.Equal(courseId, announcement.CourseId);
        Assert.Equal(instructorId, announcement.InstructorId);
        Assert.Equal("Welcome to COURSE", announcement.Title);
        Assert.Equal("This is the announcement body", announcement.Body);
        Assert.True(announcement.SendEmail);
        // X-31: nothing has actually been sent at creation time (immediate or scheduled) — dispatch is
        // now the AnnouncementDispatchJob's job, not Create's. This is a deliberate change from the old
        // assertion (Assert.Equal(now, announcement.SentAtUtc)), which encoded the bug this task fixes:
        // SentAtUtc used to be stamped at creation with nothing ever actually delivered.
        Assert.Null(announcement.SentAtUtc);
        Assert.Equal(AnnouncementDispatchStatus.Pending, announcement.DispatchStatus);
        Assert.Equal(now, announcement.CreatedAtUtc);
        Assert.Equal(0, announcement.RecipientCount);
    }

    [Fact]
    public void Create_WhenScheduled_SentAtUtcIsNull()
    {
        var now = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var scheduledAt = now.AddDays(1);

        var announcement = ANNOUNCEMENT.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Scheduled ANNOUNCEMENT",
            "Body",
            sendEmail: true,
            scheduledAtUtc: scheduledAt,
            clock);

        Assert.Null(announcement.SentAtUtc);
        Assert.Equal(scheduledAt, announcement.ScheduledAtUtc);
        Assert.Equal(AnnouncementDispatchStatus.Pending, announcement.DispatchStatus);
    }

    [Fact]
    public void MarkSent_UpdatesSentAtUtcAndRecipientCount()
    {
        var now = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var announcement = ANNOUNCEMENT.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Title",
            "Body",
            false,
            now.AddHours(2),
            clock);

        var sendTime = now.AddHours(2);
        var sendClock = new FakeClock(sendTime);
        announcement.MarkSent(42, sendClock);

        Assert.Equal(sendTime, announcement.SentAtUtc);
        Assert.Equal(42, announcement.RecipientCount);
        Assert.Equal(AnnouncementDispatchStatus.Sent, announcement.DispatchStatus);
    }

    [Fact]
    public void Create_WithEmptyTitle_ThrowsArgumentException()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        Assert.Throws<ArgumentException>(() =>
            ANNOUNCEMENT.Create(Guid.NewGuid(), Guid.NewGuid(), "", "Body", false, null, clock));
    }
}

public sealed class UserNotificationTests
{
    [Fact]
    public void Create_WhenValid_SetsPropertiesCorrectly()
    {
        var now = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var userId = Guid.NewGuid();

        var notif = USER_NOTIFICATION.Create(
            userId,
            "announcement",
            "New ANNOUNCEMENT",
            "Instructor posted a new update",
            "/courses/123",
            clock);

        Assert.NotEqual(Guid.Empty, notif.Id);
        Assert.Equal(userId, notif.UserId);
        Assert.Equal("announcement", notif.Type);
        Assert.Equal("New ANNOUNCEMENT", notif.Title);
        Assert.Equal("Instructor posted a new update", notif.Body);
        Assert.Equal("/courses/123", notif.LinkUrl);
        Assert.Null(notif.ReadAtUtc);
        Assert.Equal(now, notif.CreatedAtUtc);
    }

    [Fact]
    public void MarkRead_SetsReadAtUtc()
    {
        var now = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var notif = USER_NOTIFICATION.Create(Guid.NewGuid(), "type", "title", "body", null, clock);
        var readTime = now.AddMinutes(5);
        var readClock = new FakeClock(readTime);

        notif.MarkRead(readClock);

        Assert.Equal(readTime, notif.ReadAtUtc);
    }
}

public sealed class CreateAnnouncementValidatorTests
{
    private readonly CreateAnnouncementValidator _validator = new();

    [Fact]
    public void Validate_WhenValid_ReturnsValid()
    {
        var command = new CreateAnnouncementCommand(Guid.NewGuid(), "Title", "Body content");
        var result = _validator.Validate(command);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenCourseIdEmpty_ReturnsInvalid()
    {
        var command = new CreateAnnouncementCommand(Guid.Empty, "Title", "Body content");
        var result = _validator.Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WhenTitleEmpty_ReturnsInvalid()
    {
        var command = new CreateAnnouncementCommand(Guid.NewGuid(), "", "Body content");
        var result = _validator.Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WhenBodyEmpty_ReturnsInvalid()
    {
        var command = new CreateAnnouncementCommand(Guid.NewGuid(), "Title", "");
        var result = _validator.Validate(command);
        Assert.False(result.IsValid);
    }
}
