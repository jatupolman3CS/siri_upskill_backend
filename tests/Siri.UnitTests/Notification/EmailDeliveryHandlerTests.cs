using Microsoft.Extensions.Logging.Abstractions;
using Siri.Integrations.Email;
using Siri.Integrations.Messaging;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.SharedKernel;

namespace Siri.UnitTests.Notification;

/// <summary>
/// The Kafka email consumer's decision logic against fakes: idempotency (database status + Redis claim), outcome recording, and —
/// the part that matters most — that a mail which SMTP accepted can never be sent a second time.
/// </summary>
public class EmailDeliveryHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc);

    private sealed class Harness
    {
        public readonly List<string> Calls = [];
        public readonly FakeClock Clock = new(Now);
        public readonly FakeRepository Repository;
        public readonly FakeSender Sender;
        public readonly FakeGuard Guard;
        public readonly FakeThrottle Throttle;
        public readonly FakeHeartbeat Heartbeat = new();
        public readonly EmailDeliveryHandler Handler;

        public Harness(EMAIL_OUTBOX_MESSAGE? row, DeliveryClaim claim = DeliveryClaim.Claimed)
        {
            Repository = new FakeRepository(row, Calls);
            Sender = new FakeSender(Calls);
            Guard = new FakeGuard(claim, Calls);
            Throttle = new FakeThrottle(Calls);
            Handler = new EmailDeliveryHandler(Repository, Sender, Guard, Throttle, Clock, NullLogger<EmailDeliveryHandler>.Instance, Heartbeat);
        }
    }

    private sealed class FakeRepository(EMAIL_OUTBOX_MESSAGE? row, List<string> calls) : IEmailOutboxRepository
    {
        public Exception? ThrowOnSave { get; set; }

        public int Saves { get; private set; }

        public Task<EMAIL_OUTBOX_MESSAGE?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            calls.Add("load");
            return Task.FromResult(row is not null && row.Id == id ? row : null);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            calls.Add("save");
            Saves++;
            return ThrowOnSave is null ? Task.CompletedTask : Task.FromException(ThrowOnSave);
        }

        public Task<IReadOnlyList<EMAIL_OUTBOX_MESSAGE>> GetPendingMessagesAsync(int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(EMAIL_OUTBOX_MESSAGE message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(EMAIL_OUTBOX_MESSAGE message, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSender(List<string> calls) : IEmailSender
    {
        public Result Outcome { get; set; } = Result.Success();

        public Exception? Throw { get; set; }

        public EmailMessage? Sent { get; private set; }

        public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            calls.Add("send");
            Sent = message;
            return Throw is null ? Task.FromResult(Outcome) : Task.FromException<Result>(Throw);
        }
    }

    private sealed class FakeGuard(DeliveryClaim claim, List<string> calls) : IEmailDeliveryGuard
    {
        public bool Delivered { get; private set; }

        public bool Released { get; private set; }

        /// <summary>The token a granted claim handed out, and the one a release presented — they must be the same one.</summary>
        public const string GrantedToken = "owner-token";

        public string? ReleasedWithToken { get; private set; }

        public Task<EmailClaimTicket> TryClaimAsync(Guid messageId, CancellationToken cancellationToken)
        {
            calls.Add("claim");
            return Task.FromResult(new EmailClaimTicket(claim, claim == DeliveryClaim.Claimed ? GrantedToken : null));
        }

        public Task MarkDeliveredAsync(Guid messageId, CancellationToken cancellationToken)
        {
            calls.Add("mark-delivered");
            Delivered = true;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid messageId, string? token, CancellationToken cancellationToken)
        {
            calls.Add("release");
            Released = true;
            ReleasedWithToken = token;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHeartbeat : IEmailConsumerHeartbeat
    {
        public int Beats { get; private set; }

        public Task BeatAsync(CancellationToken cancellationToken)
        {
            Beats++;
            return Task.CompletedTask;
        }

        public Task<bool> WasActiveWithinAsync(TimeSpan window, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FakeThrottle(List<string> calls) : IEmailSendThrottle
    {
        public Task WaitForSlotAsync(CancellationToken cancellationToken)
        {
            calls.Add("throttle");
            return Task.CompletedTask;
        }
    }

    private static EMAIL_OUTBOX_MESSAGE Row() =>
        EMAIL_OUTBOX_MESSAGE.Enqueue("student@example.com", "ยืนยันอีเมล", "<p>ลิงก์</p>", "identity-email-confirmation");

    private static Result SmtpRefused() => Result.Failure(DomainError.Unavailable("550 mailbox unavailable"));

    [Fact]
    public async Task ProcessAsync_PendingRow_SendsRecordsSentAndNeverReleasesTheClaim()
    {
        var row = Row();
        var harness = new Harness(row);

        var outcome = await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.Delivered, outcome);
        Assert.Equal(EmailOutboxStatus.Sent, row.Status);
        Assert.Equal(Now, row.SentAtUtc);
        Assert.Equal("student@example.com", harness.Sender.Sent!.ToAddress);
        Assert.True(harness.Guard.Delivered);
        Assert.False(harness.Guard.Released);
    }

    [Fact]
    public async Task ProcessAsync_Success_RemembersTheDeliveryBeforeTheDatabaseIsTouchedAgain()
    {
        var row = Row();
        var harness = new Harness(row);

        await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        // The order is the whole point: once SMTP has the mail, Redis must know before the row is saved, so a crash or a failed save
        // in between cannot make the retry send it again.
        Assert.Equal(["load", "claim", "throttle", "send", "mark-delivered", "save"], harness.Calls);
    }

    [Fact]
    public async Task ProcessAsync_SmtpFailure_RecordsTheErrorSchedulesARetryAndReleasesTheClaim()
    {
        var row = Row();
        var harness = new Harness(row);
        harness.Sender.Outcome = SmtpRefused();

        var outcome = await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.FailedWillRetry, outcome);
        Assert.Equal(EmailOutboxStatus.Failed, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(Now.AddMinutes(1), row.NextRetryAtUtc);
        Assert.Contains("550", row.LastError, StringComparison.Ordinal);
        Assert.Equal(1, harness.Repository.Saves);
        Assert.True(harness.Guard.Released);
        Assert.False(harness.Guard.Delivered);
    }

    [Fact]
    public async Task ProcessAsync_LastAllowedAttemptFails_ReportsExhaustedAndLeavesNoRetry()
    {
        var row = Row();
        for (var i = 0; i < EMAIL_OUTBOX_MESSAGE.MaxAttempts - 1; i++)
        {
            row.RecordAttemptFailed("earlier failure", new FakeClock(Now.AddHours(-1)));
        }

        var harness = new Harness(row);
        harness.Sender.Outcome = SmtpRefused();

        var outcome = await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.FailedExhausted, outcome);
        Assert.True(row.IsExhausted);
        Assert.Null(row.NextRetryAtUtc);
    }

    [Fact]
    public async Task ProcessAsync_QueuedRow_IsDeliveredLikeAnyOther()
    {
        var row = Row();
        row.MarkQueued(new FakeClock(Now.AddSeconds(-2)));
        var harness = new Harness(row);

        var outcome = await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.Delivered, outcome);
        Assert.Equal(EmailOutboxStatus.Sent, row.Status);
    }

    [Fact]
    public async Task ProcessAsync_RowNoLongerExists_IsSkippedWithoutSending()
    {
        var harness = new Harness(row: null);

        var outcome = await harness.Handler.ProcessAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.Skipped, outcome);
        Assert.Equal(["load"], harness.Calls);
    }

    [Fact]
    public async Task ProcessAsync_RowAlreadySent_IsSkippedWithoutSendingAgain()
    {
        var row = Row();
        row.RecordSent(new FakeClock(Now.AddMinutes(-5)));
        var harness = new Harness(row);

        var outcome = await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.Skipped, outcome);
        Assert.Equal(["load"], harness.Calls);
    }

    [Fact]
    public async Task ProcessAsync_ExhaustedRow_IsSkipped()
    {
        var row = Row();
        for (var i = 0; i < EMAIL_OUTBOX_MESSAGE.MaxAttempts; i++)
        {
            row.RecordAttemptFailed("down", new FakeClock(Now.AddHours(-1)));
        }

        var harness = new Harness(row);

        var outcome = await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.Skipped, outcome);
        Assert.Equal(["load"], harness.Calls);
    }

    [Fact]
    public async Task ProcessAsync_RedisSaysAnEarlierRunAlreadySentIt_BringsTheRowUpToDateWithoutSending()
    {
        var row = Row();
        row.MarkQueued(new FakeClock(Now.AddMinutes(-1)));
        var harness = new Harness(row, DeliveryClaim.AlreadyDelivered);

        var outcome = await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.RecordedEarlierDelivery, outcome);
        Assert.Equal(EmailOutboxStatus.Sent, row.Status);
        Assert.Null(harness.Sender.Sent);
        Assert.Equal(["load", "claim", "save"], harness.Calls);
    }

    [Fact]
    public async Task ProcessAsync_AnotherConsumerHoldsTheClaim_DoesNothing()
    {
        var row = Row();
        var harness = new Harness(row, DeliveryClaim.InFlightElsewhere);

        var outcome = await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.InFlightElsewhere, outcome);
        Assert.Equal(EmailOutboxStatus.Pending, row.Status);
        Assert.Null(harness.Sender.Sent);
        Assert.Equal(0, harness.Repository.Saves);
    }

    [Fact]
    public async Task ProcessAsync_SenderThrows_ReleasesTheClaimAndLetsTheConsumerRetry()
    {
        var row = Row();
        var harness = new Harness(row);
        harness.Sender.Throw = new InvalidOperationException("socket exploded");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Handler.ProcessAsync(row.Id, CancellationToken.None));

        Assert.True(harness.Guard.Released);
        Assert.Equal(EmailOutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(0, harness.Heartbeat.Beats); // a record that threw is not progress
    }

    [Fact]
    public async Task ProcessAsync_SmtpFailure_ReleasesTheClaimWithTheTokenItWasGranted()
    {
        var row = Row();
        var harness = new Harness(row);
        harness.Sender.Outcome = SmtpRefused();

        await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        // Only the holder of the token may release the claim; presenting a different (or no) token must not free someone else's.
        Assert.Equal(FakeGuard.GrantedToken, harness.Guard.ReleasedWithToken);
    }

    [Theory]
    [InlineData(DeliveryClaim.Claimed)]
    [InlineData(DeliveryClaim.AlreadyDelivered)]
    [InlineData(DeliveryClaim.InFlightElsewhere)]
    public async Task ProcessAsync_AnyRecordThatWasDealtWith_CountsAsConsumerProgress(DeliveryClaim claim)
    {
        var row = Row();
        var harness = new Harness(row, claim);

        await harness.Handler.ProcessAsync(row.Id, CancellationToken.None);

        Assert.Equal(1, harness.Heartbeat.Beats);
    }

    [Fact]
    public async Task ProcessAsync_RowGone_StillCountsAsProgress()
    {
        var harness = new Harness(row: null);

        await harness.Handler.ProcessAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, harness.Heartbeat.Beats);
    }

    [Fact]
    public async Task DeliverAsync_ThePollingJobPath_DoesNotTouchTheConsumerHeartbeat()
    {
        var row = Row();
        var harness = new Harness(row);

        await harness.Handler.DeliverAsync(row, throttled: false, CancellationToken.None);

        Assert.Equal(0, harness.Heartbeat.Beats);
    }

    [Fact]
    public async Task ProcessAsync_SaveFailsAfterAMailWentOut_KeepsTheDeliveredMarkerSoTheRetryDoesNotSendAgain()
    {
        var row = Row();
        var harness = new Harness(row);
        harness.Repository.ThrowOnSave = new InvalidOperationException("database went away");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Handler.ProcessAsync(row.Id, CancellationToken.None));

        Assert.True(harness.Guard.Delivered);
        Assert.False(harness.Guard.Released);
    }

    [Fact]
    public async Task HandleAsync_PayloadNamingTheRow_DeliversThatRow()
    {
        var row = Row();
        var harness = new Harness(row);
        var record = new EmailDeliveryMessage(row.Id, row.TemplateKey, 1, Now);

        await harness.Handler.HandleAsync(
            new ConsumedMessage("topic", 0, 7, "key", record.ToJson(), new Dictionary<string, string>(), Now),
            CancellationToken.None);

        Assert.Equal(EmailOutboxStatus.Sent, row.Status);
    }

    [Fact]
    public async Task HandleAsync_MalformedPayload_IsPoisonAndTouchesNothing()
    {
        var harness = new Harness(Row());

        await Assert.ThrowsAsync<PoisonMessageException>(() => harness.Handler.HandleAsync(
            new ConsumedMessage("topic", 0, 7, "key", "this is not json", new Dictionary<string, string>(), Now),
            CancellationToken.None));

        Assert.Empty(harness.Calls);
    }
}

public class InAppNotificationHandlerTests
{
    private sealed class FakeCounter : IUnreadNotificationCounter
    {
        public List<Guid> Invalidated { get; } = [];

        public Task<int> GetAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken)
        {
            Invalidated.Add(userId);
            return Task.CompletedTask;
        }
    }

    private static ConsumedMessage Message(string value) =>
        new("topic", 0, 1, "key", value, new Dictionary<string, string>(), DateTime.UtcNow);

    [Fact]
    public async Task HandleAsync_NotificationCreated_DropsThatUsersCachedCount()
    {
        var counter = new FakeCounter();
        var userId = Guid.NewGuid();
        var record = new InAppNotificationEvent(Guid.NewGuid(), userId, "live.reminder", DateTime.UtcNow);

        await new InAppNotificationHandler(counter).HandleAsync(Message(record.ToJson()), CancellationToken.None);

        Assert.Equal([userId], counter.Invalidated);
    }

    [Fact]
    public async Task HandleAsync_SameEventTwice_IsHarmless()
    {
        var counter = new FakeCounter();
        var record = new InAppNotificationEvent(Guid.NewGuid(), Guid.NewGuid(), "x", DateTime.UtcNow);
        var handler = new InAppNotificationHandler(counter);

        await handler.HandleAsync(Message(record.ToJson()), CancellationToken.None);
        await handler.HandleAsync(Message(record.ToJson()), CancellationToken.None);

        Assert.Equal(2, counter.Invalidated.Count);
    }

    [Fact]
    public async Task HandleAsync_MalformedEvent_IsPoison()
    {
        var counter = new FakeCounter();

        await Assert.ThrowsAsync<PoisonMessageException>(() =>
            new InAppNotificationHandler(counter).HandleAsync(Message("{"), CancellationToken.None));

        Assert.Empty(counter.Invalidated);
    }
}
