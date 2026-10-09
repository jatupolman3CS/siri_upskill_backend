using Siri.Modules.Notification.Domain;

namespace Siri.UnitTests.Notification;

/// <summary>The <see cref="EmailOutboxStatus.Queued"/> leg of the outbox state machine: a row handed to the Kafka relay.</summary>
public class EmailOutboxMessageQueueTests
{
    private static readonly DateTime T0 = new(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc);

    private static EMAIL_OUTBOX_MESSAGE NewMessage() =>
        EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "Subject", "<p>Body</p>", "template");

    private static EMAIL_OUTBOX_MESSAGE ExhaustedMessage(FakeClock clock)
    {
        var message = NewMessage();
        for (var i = 0; i < EMAIL_OUTBOX_MESSAGE.MaxAttempts; i++)
        {
            message.RecordAttemptFailed("smtp down", clock);
        }

        return message;
    }

    [Fact]
    public void MarkQueued_PendingMessage_BecomesQueuedAndRecordsWhen()
    {
        var message = NewMessage();

        message.MarkQueued(new FakeClock(T0));

        Assert.Equal(EmailOutboxStatus.Queued, message.Status);
        Assert.Equal(T0, message.QueuedAtUtc);
        Assert.Equal(0, message.Attempts);
    }

    [Fact]
    public void MarkQueued_FailedMessageWithRetryDue_ClearsTheRetryTimeAndKeepsAttempts()
    {
        var clock = new FakeClock(T0);
        var message = NewMessage();
        message.RecordAttemptFailed("smtp down", clock);
        Assert.NotNull(message.NextRetryAtUtc);

        clock.UtcNow = T0.AddMinutes(2);
        message.MarkQueued(clock);

        Assert.Equal(EmailOutboxStatus.Queued, message.Status);
        Assert.Null(message.NextRetryAtUtc);
        Assert.Equal(1, message.Attempts);
        Assert.Equal(T0.AddMinutes(2), message.QueuedAtUtc);
    }

    [Fact]
    public void MarkQueued_AlreadyQueued_RepublishRefreshesTheTimestamp()
    {
        var clock = new FakeClock(T0);
        var message = NewMessage();
        message.MarkQueued(clock);

        clock.UtcNow = T0.AddMinutes(20);
        message.MarkQueued(clock);

        Assert.Equal(EmailOutboxStatus.Queued, message.Status);
        Assert.Equal(T0.AddMinutes(20), message.QueuedAtUtc);
    }

    [Fact]
    public void MarkQueued_SentMessage_Throws()
    {
        var clock = new FakeClock(T0);
        var message = NewMessage();
        message.RecordSent(clock);

        Assert.Throws<InvalidOperationException>(() => message.MarkQueued(clock));
    }

    [Fact]
    public void MarkQueued_ExhaustedMessage_Throws()
    {
        var clock = new FakeClock(T0);
        var message = ExhaustedMessage(clock);
        Assert.True(message.IsExhausted);

        Assert.Throws<InvalidOperationException>(() => message.MarkQueued(clock));
    }

    [Fact]
    public void RecordSent_QueuedMessage_BecomesSent()
    {
        var clock = new FakeClock(T0);
        var message = NewMessage();
        message.MarkQueued(clock);

        message.RecordSent(clock);

        Assert.Equal(EmailOutboxStatus.Sent, message.Status);
        Assert.Equal(T0, message.SentAtUtc);
    }

    [Fact]
    public void RecordAttemptFailed_QueuedMessage_SchedulesTheRetryAndLeavesTheQueue()
    {
        var clock = new FakeClock(T0);
        var message = NewMessage();
        message.MarkQueued(clock);

        message.RecordAttemptFailed("smtp down", clock);

        Assert.Equal(EmailOutboxStatus.Failed, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.Equal(T0.AddMinutes(1), message.NextRetryAtUtc);
        Assert.False(message.IsExhausted);
    }

    [Fact]
    public void RecordAttemptFailed_FifthFailureOfAQueuedMessage_ExhaustsIt()
    {
        var clock = new FakeClock(T0);
        var message = NewMessage();
        for (var i = 0; i < EMAIL_OUTBOX_MESSAGE.MaxAttempts - 1; i++)
        {
            message.RecordAttemptFailed("smtp down", clock);
        }

        clock.UtcNow = T0.AddHours(1);
        message.MarkQueued(clock);
        message.RecordAttemptFailed("smtp still down", clock);

        Assert.True(message.IsExhausted);
        Assert.Null(message.NextRetryAtUtc);
    }

    [Fact]
    public void IsExhausted_PendingFailedAndSentMessages_IsFalse()
    {
        var clock = new FakeClock(T0);

        var pending = NewMessage();
        var failed = NewMessage();
        failed.RecordAttemptFailed("smtp down", clock);
        var sent = NewMessage();
        sent.RecordSent(clock);

        Assert.False(pending.IsExhausted);
        Assert.False(failed.IsExhausted);
        Assert.False(sent.IsExhausted);
    }

    [Fact]
    public void MarkQueued_NullClock_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => NewMessage().MarkQueued(null!));
    }

    // ---- RevertQueued: the compensation when the broker did not accept the record ----

    [Fact]
    public void RevertQueued_ClaimedPendingMessage_GoesBackToPending()
    {
        var message = NewMessage();
        message.MarkQueued(new FakeClock(T0));

        var reverted = message.RevertQueued(EmailOutboxStatus.Pending, previousNextRetryAtUtc: null, previousQueuedAtUtc: null, queuedAtUtc: T0);

        Assert.True(reverted);
        Assert.Equal(EmailOutboxStatus.Pending, message.Status);
        Assert.Null(message.QueuedAtUtc);
        Assert.Null(message.NextRetryAtUtc);
    }

    [Fact]
    public void RevertQueued_ClaimedRetryDueMessage_GetsItsRetryTimeBackSoItIsDueAgainAtOnce()
    {
        var clock = new FakeClock(T0);
        var message = NewMessage();
        message.RecordAttemptFailed("smtp down", clock);
        var dueAt = message.NextRetryAtUtc;
        clock.UtcNow = T0.AddMinutes(2);
        message.MarkQueued(clock);

        var reverted = message.RevertQueued(EmailOutboxStatus.Failed, dueAt, previousQueuedAtUtc: null, queuedAtUtc: T0.AddMinutes(2));

        Assert.True(reverted);
        Assert.Equal(EmailOutboxStatus.Failed, message.Status);
        Assert.Equal(dueAt, message.NextRetryAtUtc);
        Assert.Equal(1, message.Attempts);
    }

    [Fact]
    public void RevertQueued_ClaimedStaleQueuedMessage_GetsItsOldQueueTimeBack()
    {
        var clock = new FakeClock(T0);
        var message = NewMessage();
        message.MarkQueued(clock);
        clock.UtcNow = T0.AddHours(1);
        message.MarkQueued(clock); // re-claimed as stale

        message.RevertQueued(EmailOutboxStatus.Queued, previousNextRetryAtUtc: null, previousQueuedAtUtc: T0, queuedAtUtc: T0.AddHours(1));

        Assert.Equal(EmailOutboxStatus.Queued, message.Status);
        Assert.Equal(T0, message.QueuedAtUtc);
    }

    [Fact]
    public void RevertQueued_ConsumerAlreadyRecordedAnOutcome_DoesNothing()
    {
        var clock = new FakeClock(T0);
        var message = NewMessage();
        message.MarkQueued(clock);
        message.RecordSent(clock);

        var reverted = message.RevertQueued(EmailOutboxStatus.Pending, null, null, queuedAtUtc: T0);

        Assert.False(reverted);
        Assert.Equal(EmailOutboxStatus.Sent, message.Status);
    }

    [Fact]
    public void RevertQueued_AnotherClaimOwnsTheRowNow_DoesNothing()
    {
        var clock = new FakeClock(T0);
        var message = NewMessage();
        message.MarkQueued(clock);
        clock.UtcNow = T0.AddHours(1);
        message.MarkQueued(clock); // a later claim, with a different stamp

        var reverted = message.RevertQueued(EmailOutboxStatus.Pending, null, null, queuedAtUtc: T0);

        Assert.False(reverted);
        Assert.Equal(EmailOutboxStatus.Queued, message.Status);
        Assert.Equal(T0.AddHours(1), message.QueuedAtUtc);
    }

    [Theory]
    [InlineData(EmailOutboxStatus.Sent)]
    public void RevertQueued_ImpossiblePreviousStatus_Throws(EmailOutboxStatus previous)
    {
        var message = NewMessage();
        message.MarkQueued(new FakeClock(T0));

        Assert.Throws<ArgumentOutOfRangeException>(() => message.RevertQueued(previous, null, null, T0));
    }
}
