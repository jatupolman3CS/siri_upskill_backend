using Siri.Modules.Notification.Domain;

namespace Siri.UnitTests.Notification;

public class EmailOutboxMessageTests
{
    [Fact]
    public void Enqueue_ValidInput_ReturnsMessageInPendingStatus()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue(
            "student@example.com", "ยินดีต้อนรับ", "<p>สวัสดี</p>", "welcome-email");

        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal("student@example.com", message.ToEmail);
        Assert.Equal("ยินดีต้อนรับ", message.Subject);
        Assert.Equal("<p>สวัสดี</p>", message.BodyHtml);
        Assert.Equal("welcome-email", message.TemplateKey);
        Assert.Equal(EmailOutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.NextRetryAtUtc);
        Assert.Null(message.SentAtUtc);
        Assert.Null(message.LastError);
    }

    [Fact]
    public void Enqueue_NoTemplateKey_AllowsNullTemplateKey()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "Subject", "<p>Body</p>", null);

        Assert.Null(message.TemplateKey);
    }

    [Theory]
    [InlineData("", "Subject", "<p>Body</p>")]
    [InlineData("student@example.com", "", "<p>Body</p>")]
    [InlineData("student@example.com", "Subject", "")]
    public void Enqueue_MissingRequiredField_ThrowsArgumentException(string toEmail, string subject, string bodyHtml)
    {
        Assert.Throws<ArgumentException>(() => EMAIL_OUTBOX_MESSAGE.Enqueue(toEmail, subject, bodyHtml, null));
    }

    [Fact]
    public void RecordAttemptFailed_FirstFailure_IncrementsAttemptsAndSchedulesRetryOneMinuteLater()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "Subject", "<p>Body</p>", null);
        var clock = new FakeClock(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));

        message.RecordAttemptFailed("SMTP timeout", clock);

        Assert.Equal(1, message.Attempts);
        Assert.Equal(EmailOutboxStatus.Failed, message.Status);
        Assert.Equal("SMTP timeout", message.LastError);
        Assert.Equal(clock.UtcNow.AddMinutes(1), message.NextRetryAtUtc);
    }

    [Fact]
    public void RecordAttemptFailed_RepeatedFailures_DoublesBackoffEachTime()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "Subject", "<p>Body</p>", null);
        var clock = new FakeClock(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));

        message.RecordAttemptFailed("error 1", clock); // attempt 1 -> +1 min
        Assert.Equal(clock.UtcNow.AddMinutes(1), message.NextRetryAtUtc);

        message.RecordAttemptFailed("error 2", clock); // attempt 2 -> +2 min
        Assert.Equal(clock.UtcNow.AddMinutes(2), message.NextRetryAtUtc);

        message.RecordAttemptFailed("error 3", clock); // attempt 3 -> +4 min
        Assert.Equal(clock.UtcNow.AddMinutes(4), message.NextRetryAtUtc);

        message.RecordAttemptFailed("error 4", clock); // attempt 4 -> +8 min
        Assert.Equal(clock.UtcNow.AddMinutes(8), message.NextRetryAtUtc);

        Assert.Equal(4, message.Attempts);
        Assert.Equal(EmailOutboxStatus.Failed, message.Status);
    }

    [Fact]
    public void RecordAttemptFailed_ReachesMaxAttempts_TransitionsToTerminalFailedWithNoNextRetry()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "Subject", "<p>Body</p>", null);
        var clock = new FakeClock(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));

        for (var i = 0; i < EMAIL_OUTBOX_MESSAGE.MaxAttempts; i++)
        {
            message.RecordAttemptFailed($"error {i + 1}", clock);
        }

        Assert.Equal(EMAIL_OUTBOX_MESSAGE.MaxAttempts, message.Attempts);
        Assert.Equal(EmailOutboxStatus.Failed, message.Status);
        Assert.Null(message.NextRetryAtUtc); // terminal — sender job's query stops matching this row
        Assert.Equal($"error {EMAIL_OUTBOX_MESSAGE.MaxAttempts}", message.LastError);
    }

    [Fact]
    public void RecordAttemptFailed_AlreadySentMessage_ThrowsInvalidOperationException()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "Subject", "<p>Body</p>", null);
        message.RecordSent(new FakeClock(DateTime.UtcNow));

        Assert.Throws<InvalidOperationException>(() => message.RecordAttemptFailed("error", new FakeClock(DateTime.UtcNow)));
    }

    [Fact]
    public void RecordSent_PendingMessage_SetsSentAtUtcAndTerminalSentStatus()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "Subject", "<p>Body</p>", null);
        var clock = new FakeClock(new DateTime(2026, 8, 17, 11, 0, 0, DateTimeKind.Utc));

        message.RecordSent(clock);

        Assert.Equal(EmailOutboxStatus.Sent, message.Status);
        Assert.Equal(clock.UtcNow, message.SentAtUtc);
        Assert.Null(message.NextRetryAtUtc);
    }

    [Fact]
    public void RecordSent_AfterARetryableFailure_SucceedsAndClearsNextRetryAtUtc()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "Subject", "<p>Body</p>", null);
        var clock = new FakeClock(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));
        message.RecordAttemptFailed("transient error", clock);

        message.RecordSent(clock);

        Assert.Equal(EmailOutboxStatus.Sent, message.Status);
        Assert.Null(message.NextRetryAtUtc);
    }

    [Fact]
    public void RecordSent_AlreadySentMessage_IsIdempotentAndKeepsOriginalSentAtUtc()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "Subject", "<p>Body</p>", null);
        var firstSend = new FakeClock(new DateTime(2026, 8, 17, 11, 0, 0, DateTimeKind.Utc));
        message.RecordSent(firstSend);

        message.RecordSent(new FakeClock(new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(firstSend.UtcNow, message.SentAtUtc);
    }

    [Fact]
    public void RecordSent_MessageThatExhaustedAllRetries_ThrowsInvalidOperationException()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "Subject", "<p>Body</p>", null);
        var clock = new FakeClock(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));
        for (var i = 0; i < EMAIL_OUTBOX_MESSAGE.MaxAttempts; i++)
        {
            message.RecordAttemptFailed($"error {i + 1}", clock);
        }

        Assert.Throws<InvalidOperationException>(() => message.RecordSent(clock));
    }
}
