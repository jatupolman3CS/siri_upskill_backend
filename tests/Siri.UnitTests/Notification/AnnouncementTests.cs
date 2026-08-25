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

        var announcement = Announcement.Create(
            courseId,
            instructorId,
            "Welcome to Course",
            "This is the announcement body",
            sendEmail: true,
            scheduledAtUtc: null,
            clock);

        Assert.NotEqual(Guid.Empty, announcement.Id);
        Assert.Equal(courseId, announcement.CourseId);
        Assert.Equal(instructorId, announcement.InstructorId);
        Assert.Equal("Welcome to Course", announcement.Title);
        Assert.Equal("This is the announcement body", announcement.Body);
        Assert.True(announcement.SendEmail);
        Assert.Equal(now, announcement.SentAtUtc);
        Assert.Equal(now, announcement.CreatedAtUtc);
        Assert.Equal(0, announcement.RecipientCount);
    }

    [Fact]
    public void Create_WhenScheduled_SentAtUtcIsNull()
    {
        var now = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var scheduledAt = now.AddDays(1);

        var announcement = Announcement.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Scheduled Announcement",
            "Body",
            sendEmail: true,
            scheduledAtUtc: scheduledAt,
            clock);

        Assert.Null(announcement.SentAtUtc);
        Assert.Equal(scheduledAt, announcement.ScheduledAtUtc);
    }

    [Fact]
    public void MarkSent_UpdatesSentAtUtcAndRecipientCount()
    {
        var now = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var announcement = Announcement.Create(
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
    }

    [Fact]
    public void Create_WithEmptyTitle_ThrowsArgumentException()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        Assert.Throws<ArgumentException>(() =>
            Announcement.Create(Guid.NewGuid(), Guid.NewGuid(), "", "Body", false, null, clock));
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

        var notif = UserNotification.Create(
            userId,
            "announcement",
            "New Announcement",
            "Instructor posted a new update",
            "/courses/123",
            clock);

        Assert.NotEqual(Guid.Empty, notif.Id);
        Assert.Equal(userId, notif.UserId);
        Assert.Equal("announcement", notif.Type);
        Assert.Equal("New Announcement", notif.Title);
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

        var notif = UserNotification.Create(Guid.NewGuid(), "type", "title", "body", null, clock);
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
