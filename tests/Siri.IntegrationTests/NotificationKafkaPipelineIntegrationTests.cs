using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Siri.Integrations.Email;
using Siri.Integrations.Messaging;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Notification;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using StackExchange.Redis;

namespace Siri.IntegrationTests;

/// <summary>
/// The whole notification pipeline end to end on real infrastructure: PostgreSQL outbox → relay → Kafka → consumer → (recording) SMTP
/// sender, plus Redis for the duplicate guard and the unread-count cache. Each test starts its own copy of the pipeline on topics
/// and a consumer group carrying a random suffix, so tests never see each other's records; the database is shared with the rest of
/// the suite, so every assertion is about rows the test itself created.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class NotificationKafkaPipelineIntegrationTests : IClassFixture<KafkaFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    private readonly ContainersFixture _containers;
    private readonly KafkaFixture _kafka;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private readonly List<Pipeline> _pipelines = [];

    public NotificationKafkaPipelineIntegrationTests(ContainersFixture containers, KafkaFixture kafka)
    {
        _containers = containers;
        _kafka = kafka;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var pipeline in _pipelines)
        {
            await pipeline.DisposeAsync();
        }
    }

    // ---- infrastructure for the tests ----

    private sealed class RecordingSender : IEmailSender
    {
        private readonly ConcurrentDictionary<string, int> _attempts = new();
        private readonly ConcurrentDictionary<string, int> _delivered = new();

        /// <summary>Decides the outcome of the Nth attempt (1-based) to an address.</summary>
        public Func<string, int, Result> Behavior { get; set; } = (_, _) => Result.Success();

        public int AttemptsTo(string address) => _attempts.GetValueOrDefault(address);

        public int DeliveriesTo(string address) => _delivered.GetValueOrDefault(address);

        /// <summary>Makes the Nth attempt to an address <i>throw</i> (an infrastructure failure, unlike <see cref="Behavior"/>'s business refusal).</summary>
        public Func<string, int, Exception?> ThrowWhen { get; set; } = (_, _) => null;

        public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            var attempt = _attempts.AddOrUpdate(message.ToAddress, 1, (_, n) => n + 1);
            if (ThrowWhen(message.ToAddress, attempt) is { } failure)
            {
                throw failure;
            }

            var result = Behavior(message.ToAddress, attempt);
            if (result.IsSuccess)
            {
                _delivered.AddOrUpdate(message.ToAddress, 1, (_, n) => n + 1);
            }

            return Task.FromResult(result);
        }
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    private sealed class Pipeline(ServiceProvider provider, List<IHostedService> hosted, RecordingSender sender, NotificationTopics topics)
        : IAsyncDisposable
    {
        public ServiceProvider Provider { get; } = provider;

        public RecordingSender Sender { get; } = sender;

        public NotificationTopics Topics { get; } = topics;

        public async ValueTask DisposeAsync()
        {
            using var stopBudget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            foreach (var service in hosted)
            {
                try
                {
                    await service.StopAsync(stopBudget.Token);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown is best-effort in a test; the provider disposal below releases the sockets regardless.
                }
            }

            await Provider.DisposeAsync();
        }
    }

    private async Task<Pipeline> StartPipelineAsync(
        Func<string, int, Result>? sendBehavior = null,
        string? bootstrapServers = null,
        int produceTimeoutSeconds = 15,
        Func<string, int, Exception?>? sendThrows = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
                ["Redis:ConnectionString"] = _containers.RedisConnectionString,
                ["Email:Provider"] = "Log",
                ["Kafka:BootstrapServers"] = bootstrapServers ?? _kafka.BootstrapServers,
                ["Kafka:AllowPlaintext"] = "true",
                ["Kafka:ProduceTimeoutSeconds"] = produceTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                // A prefix unique to this test: its topics and consumer groups can never see another test's (or another run's) records.
                ["Kafka:TopicPrefix"] = $"{KafkaFixture.TestTopicPrefix}{_suffix}",
                ["Notification:Delivery:Transport"] = "Kafka",
                ["Notification:Delivery:Partitions"] = "2",
                ["Notification:Delivery:RelayPollIntervalMs"] = "100",
                ["Notification:Delivery:UnreadCountCacheSeconds"] = "600",
            })
            .AddInMemoryCollection(bootstrapServers is null ? _kafka.ExtraConfiguration : new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            // Quiet by default; SIRI_IT_VERBOSE=1 prints what the relays and consumers are doing, for debugging a failing run.
            var verbose = Environment.GetEnvironmentVariable("SIRI_IT_VERBOSE") == "1";
            logging.SetMinimumLevel(verbose ? LogLevel.Information : LogLevel.Warning);
            if (verbose)
            {
                logging.AddConsole();
            }
        });
        services.AddPersistence(configuration);
        services.AddSharedRedis(configuration);
        services.AddNotificationModule(configuration);

        var sender = new RecordingSender();
        if (sendBehavior is not null)
        {
            sender.Behavior = sendBehavior;
        }

        if (sendThrows is not null)
        {
            sender.ThrowWhen = sendThrows;
        }

        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender>(sender);
        services.AddNotificationDelivery(configuration);

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }

        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }

        var pipeline = new Pipeline(provider, hosted, sender, provider.GetRequiredService<NotificationTopics>());
        _pipelines.Add(pipeline);
        return pipeline;
    }

    private static string NewAddress() => $"kafka-{Guid.NewGuid():N}@example.test";

    private async Task<Guid> StageEmailAsync(Pipeline pipeline, string address, string subject = "Pipeline test")
    {
        await using var scope = pipeline.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue(address, subject, "<p>secret-reset-link-do-not-leak</p>", "it-template");
        db.EmailOutboxMessages().Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private static async Task<EMAIL_OUTBOX_MESSAGE> LoadAsync(Pipeline pipeline, Guid id)
    {
        await using var scope = pipeline.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .EmailOutboxMessages().AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Patience);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Timed out waiting for: {because}");
    }

    private static Task WaitForStatusAsync(Pipeline pipeline, Guid id, EmailOutboxStatus status) =>
        WaitUntilAsync(async () => (await LoadAsync(pipeline, id)).Status == status, $"email {id} to reach {status}");

    // ---- email: the happy path and what travels through the broker ----

    [Fact]
    public async Task Email_StagedAsAnOutboxRow_IsRelayedConsumedSentAndRecorded()
    {
        var pipeline = await StartPipelineAsync();
        var address = NewAddress();

        var id = await StageEmailAsync(pipeline, address);
        await WaitForStatusAsync(pipeline, id, EmailOutboxStatus.Sent);

        var row = await LoadAsync(pipeline, id);
        Assert.NotNull(row.SentAtUtc);
        Assert.NotNull(row.QueuedAtUtc);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(1, pipeline.Sender.DeliveriesTo(address));

        await using var scope = pipeline.Provider.CreateAsyncScope();
        var delivered = await scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase()
            .StringGetAsync(RedisEmailDeliveryGuard.KeyFor(id));
        Assert.Equal("delivered", (string?)delivered);

        // Nothing sends it a second time.
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(1, pipeline.Sender.DeliveriesTo(address));
    }

    [Fact]
    public async Task Email_WhatTheBrokerCarries_IsAClaimCheckWithNoBodySubjectOrAddress()
    {
        var pipeline = await StartPipelineAsync();
        var address = NewAddress();

        var id = await StageEmailAsync(pipeline, address, subject: "A subject nobody on the broker should read");
        await WaitForStatusAsync(pipeline, id, EmailOutboxStatus.Sent);

        var records = _kafka.ReadAll(pipeline.Topics.Email, TimeSpan.FromSeconds(20));
        var mine = records.Where(r => r.Message.Value.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(mine);
        foreach (var record in mine)
        {
            Assert.DoesNotContain("secret-reset-link", record.Message.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("subject nobody", record.Message.Value, StringComparison.Ordinal);
            Assert.DoesNotContain(address, record.Message.Value, StringComparison.Ordinal);
            Assert.DoesNotContain(address, record.Message.Key, StringComparison.Ordinal);
            Assert.Equal(EmailDeliveryMessage.KeyFor(id), record.Message.Key);
        }
    }

    [Fact]
    public async Task Email_ManyRecipients_AreAllDeliveredExactlyOnce()
    {
        var pipeline = await StartPipelineAsync();
        var addresses = Enumerable.Range(0, 30).Select(_ => NewAddress()).ToList();

        var ids = new List<Guid>();
        foreach (var address in addresses)
        {
            ids.Add(await StageEmailAsync(pipeline, address));
        }

        await WaitUntilAsync(
            async () =>
            {
                foreach (var id in ids)
                {
                    if ((await LoadAsync(pipeline, id)).Status != EmailOutboxStatus.Sent)
                    {
                        return false;
                    }
                }

                return true;
            },
            "all 30 emails to be sent");

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.All(addresses, address => Assert.Equal(1, pipeline.Sender.DeliveriesTo(address)));
    }

    // ---- email: failure, retry, exhaustion ----

    [Fact]
    public async Task Email_SmtpFailsOnce_IsRetriedThroughTheRelayAndThenSent()
    {
        var pipeline = await StartPipelineAsync((_, attempt) =>
            attempt == 1 ? Result.Failure(DomainError.Unavailable("450 try later")) : Result.Success());
        var address = NewAddress();

        var id = await StageEmailAsync(pipeline, address);

        await WaitUntilAsync(
            async () =>
            {
                var row = await LoadAsync(pipeline, id);
                return row.Status == EmailOutboxStatus.Failed && row.NextRetryAtUtc is not null;
            },
            "the first attempt to fail and a retry to be scheduled");

        var failed = await LoadAsync(pipeline, id);
        Assert.Equal(1, failed.Attempts);
        Assert.Contains("450", failed.LastError, StringComparison.Ordinal);

        await FastForwardRetryAsync(pipeline, id);
        await WaitForStatusAsync(pipeline, id, EmailOutboxStatus.Sent);

        Assert.Equal(2, pipeline.Sender.AttemptsTo(address));
        Assert.Equal(1, pipeline.Sender.DeliveriesTo(address));
    }

    [Fact]
    public async Task Email_FailsEveryTime_EndsExhaustedAfterTheMaximumAttemptsAndIsNeverRetriedAgain()
    {
        var pipeline = await StartPipelineAsync((_, _) => Result.Failure(DomainError.Unavailable("550 mailbox unavailable")));
        var address = NewAddress();

        var id = await StageEmailAsync(pipeline, address);

        for (var attempt = 1; attempt < EMAIL_OUTBOX_MESSAGE.MaxAttempts; attempt++)
        {
            var expected = attempt;
            await WaitUntilAsync(
                async () =>
                {
                    var row = await LoadAsync(pipeline, id);
                    return row.Status == EmailOutboxStatus.Failed && row.Attempts == expected && row.NextRetryAtUtc is not null;
                },
                $"attempt {attempt} to fail with a retry scheduled");
            await FastForwardRetryAsync(pipeline, id);
        }

        await WaitUntilAsync(async () => (await LoadAsync(pipeline, id)).IsExhausted, "the last attempt to exhaust the message");

        var exhausted = await LoadAsync(pipeline, id);
        Assert.Equal(EMAIL_OUTBOX_MESSAGE.MaxAttempts, exhausted.Attempts);
        Assert.Null(exhausted.NextRetryAtUtc);
        Assert.Equal(EMAIL_OUTBOX_MESSAGE.MaxAttempts, pipeline.Sender.AttemptsTo(address));

        // A dead letter stays dead: no relay publishes it and no consumer touches it.
        await Task.Delay(TimeSpan.FromSeconds(4));
        Assert.Equal(EMAIL_OUTBOX_MESSAGE.MaxAttempts, pipeline.Sender.AttemptsTo(address));
        Assert.True((await LoadAsync(pipeline, id)).IsExhausted);
    }

    private static async Task FastForwardRetryAsync(Pipeline pipeline, Guid id)
    {
        await using var scope = pipeline.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailOutboxMessages()
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.NextRetryAtUtc, DateTime.UtcNow.AddMinutes(-1)));
    }

    // ---- email: duplicates, poison, lost records, outages ----

    [Fact]
    public async Task Email_RecordDeliveredTwice_IsStillSentOnce()
    {
        var pipeline = await StartPipelineAsync();
        var address = NewAddress();
        var id = await StageEmailAsync(pipeline, address);
        await WaitForStatusAsync(pipeline, id, EmailOutboxStatus.Sent);

        // The broker redelivers (a rebalance, a crash before the offset commit): simulate it with two more copies of the record.
        var producer = pipeline.Provider.GetRequiredService<IMessageProducer>();
        var duplicate = new EmailDeliveryMessage(id, "it-template", 1, DateTime.UtcNow).ToJson();
        await producer.ProduceAsync(pipeline.Topics.Email, EmailDeliveryMessage.KeyFor(id), duplicate, null, CancellationToken.None);
        await producer.ProduceAsync(pipeline.Topics.Email, EmailDeliveryMessage.KeyFor(id), duplicate, null, CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(1, pipeline.Sender.DeliveriesTo(address));
        Assert.Equal(EmailOutboxStatus.Sent, (await LoadAsync(pipeline, id)).Status);
    }

    [Fact]
    public async Task Email_PoisonRecord_IsDeadLetteredWithItsReasonAndDoesNotBlockTheNextOne()
    {
        var pipeline = await StartPipelineAsync();
        var producer = pipeline.Provider.GetRequiredService<IMessageProducer>();
        await producer.ProduceAsync(pipeline.Topics.Email, "poison-key", "this is not json", null, CancellationToken.None);

        var address = NewAddress();
        var id = await StageEmailAsync(pipeline, address);
        await WaitForStatusAsync(pipeline, id, EmailOutboxStatus.Sent);

        DeadLetterEnvelope? envelope = null;
        await WaitUntilAsync(
            () =>
            {
                var records = _kafka.ReadAll(pipeline.Topics.EmailDeadLetter, TimeSpan.FromSeconds(5));
                envelope = records.Select(r => DeadLetterEnvelope.FromJson(r.Message.Value)).FirstOrDefault(e => e.OriginalValue == "this is not json");
                return Task.FromResult(envelope is not null);
            },
            "the poison record to appear on the dead-letter topic");

        Assert.Equal(pipeline.Topics.Email, envelope!.SourceTopic);
        Assert.Equal("poison-key", envelope.Key);
        Assert.Contains("JSON", envelope.Reason, StringComparison.Ordinal);
        Assert.Equal(1, pipeline.Sender.DeliveriesTo(address));
    }

    [Fact]
    public async Task Email_InfrastructureFailureOnce_TheConsumerRetriesTheRecordInPlaceAndDeliversOnce()
    {
        var address = NewAddress();
        var pipeline = await StartPipelineAsync(sendThrows: (to, attempt) =>
            to == address && attempt == 1 ? new InvalidOperationException("connection reset by peer") : null);

        var id = await StageEmailAsync(pipeline, address);
        await WaitForStatusAsync(pipeline, id, EmailOutboxStatus.Sent);

        // The throw is not a business refusal: no retry budget is spent, the same record is simply tried again a moment later.
        var row = await LoadAsync(pipeline, id);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(2, pipeline.Sender.AttemptsTo(address));
        Assert.Equal(1, pipeline.Sender.DeliveriesTo(address));

        var deadLetters = _kafka.ReadAll(pipeline.Topics.EmailDeadLetter, TimeSpan.FromSeconds(5));
        Assert.Empty(deadLetters);
    }

    [Fact]
    public async Task Email_InfrastructureKeepsFailing_AfterFiveAttemptsTheRecordIsDeadLetteredAndTheRowStaysSafelyQueued()
    {
        var address = NewAddress();
        var pipeline = await StartPipelineAsync(sendThrows: (to, _) =>
            to == address ? new InvalidOperationException("smtp host unreachable") : null);

        var id = await StageEmailAsync(pipeline, address);

        DeadLetterEnvelope? envelope = null;
        await WaitUntilAsync(
            () =>
            {
                var records = _kafka.ReadAll(pipeline.Topics.EmailDeadLetter, TimeSpan.FromSeconds(5));
                envelope = records.Select(r => DeadLetterEnvelope.FromJson(r.Message.Value))
                    .FirstOrDefault(e => e.OriginalValue.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(envelope is not null);
            },
            "the record to be dead-lettered after the in-place retries");

        Assert.Equal(5, envelope!.Attempts);
        Assert.Contains("InvalidOperationException", envelope.Reason, StringComparison.Ordinal);
        Assert.Equal(5, pipeline.Sender.AttemptsTo(address));
        Assert.Equal(0, pipeline.Sender.DeliveriesTo(address));

        // Nothing was lost: the row is still waiting (Queued) and the claim was released, so the stale-window sweep can deliver it later.
        var row = await LoadAsync(pipeline, id);
        Assert.Equal(EmailOutboxStatus.Queued, row.Status);
        await using var scope = pipeline.Provider.CreateAsyncScope();
        var marker = await scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase()
            .StringGetAsync(RedisEmailDeliveryGuard.KeyFor(id));
        Assert.False(marker.HasValue, "a failed attempt must not leave its claim behind");
    }

    [Fact]
    public async Task Email_RowStuckQueuedPastTheStaleWindow_IsPublishedAgainAndDelivered()
    {
        // A record that was published but whose consumer died before recording any outcome: the row says Queued, an hour ago.
        var address = NewAddress();
        var stuck = EMAIL_OUTBOX_MESSAGE.Enqueue(address, "stuck", "<p>x</p>", "it-template");
        stuck.MarkQueued(new FixedClock(DateTime.UtcNow.AddHours(-1)));

        var pipeline = await StartPipelineAsync();
        await using (var scope = pipeline.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EmailOutboxMessages().Add(stuck);
            await db.SaveChangesAsync();
        }

        await WaitForStatusAsync(pipeline, stuck.Id, EmailOutboxStatus.Sent);
        Assert.Equal(1, pipeline.Sender.DeliveriesTo(address));
    }

    [Fact]
    public async Task Email_RowsWaitingBehindABacklog_AreLeftAloneWhileConsumersAreWorking_AndSweptOnceTheyGoQuiet()
    {
        // A row that has sat in Queued for an hour looks "lost" — unless the consumers are demonstrably busy, in which case it is just waiting its
        // turn in Kafka behind a long backlog and must not be published a second (third, fourth...) time.
        var address = NewAddress();
        var stuck = EMAIL_OUTBOX_MESSAGE.Enqueue(address, "waiting", "<p>x</p>", "it-template");
        stuck.MarkQueued(new FixedClock(DateTime.UtcNow.AddHours(-1)));

        var pipeline = await StartPipelineAsync();
        await pipeline.Provider.GetRequiredService<IEmailConsumerHeartbeat>().BeatAsync(CancellationToken.None);

        await using (var scope = pipeline.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EmailOutboxMessages().Add(stuck);
            await db.SaveChangesAsync();
        }

        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.Equal(EmailOutboxStatus.Queued, (await LoadAsync(pipeline, stuck.Id)).Status);
        Assert.Equal(0, pipeline.Sender.AttemptsTo(address));

        // The consumers go quiet (here: the heartbeat is wiped): now the old row is genuinely suspicious and is claimed again.
        await using (var scope = pipeline.Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase()
                .KeyDeleteAsync($"notify:email:heartbeat:{pipeline.Topics.EmailGroup}");
        }

        await WaitForStatusAsync(pipeline, stuck.Id, EmailOutboxStatus.Sent);
        Assert.Equal(1, pipeline.Sender.DeliveriesTo(address));
    }

    [Fact]
    public async Task Email_BrokerUnreachable_LeavesRowsPendingAndTheHostRunning()
    {
        var pipeline = await StartPipelineAsync(bootstrapServers: "127.0.0.1:1", produceTimeoutSeconds: 1);
        var address = NewAddress();

        var id = await StageEmailAsync(pipeline, address);
        await Task.Delay(TimeSpan.FromSeconds(8)); // several failed relay cycles, with back-off

        // Each cycle claims the row (Queued), fails to publish and puts it back; between cycles it is Pending again. Look until it is.
        await WaitUntilAsync(
            async () =>
            {
                var row = await LoadAsync(pipeline, id);
                return row.Status == EmailOutboxStatus.Pending && row.QueuedAtUtc is null;
            },
            "the unpublished row to be released back to Pending",
            TimeSpan.FromSeconds(20));

        Assert.Equal(0, (await LoadAsync(pipeline, id)).Attempts);
        Assert.Equal(0, pipeline.Sender.AttemptsTo(address));
    }

    // ---- in-app events ----

    [Fact]
    public async Task InApp_NotificationCreated_DropsTheCachedUnreadCountThroughTheBroker()
    {
        var pipeline = await StartPipelineAsync();
        var userId = Guid.NewGuid();

        await using var scope = pipeline.Provider.CreateAsyncScope();
        var counter = scope.ServiceProvider.GetRequiredService<IUnreadNotificationCounter>();
        Assert.Equal(0, await counter.GetAsync(userId, CancellationToken.None)); // cached for 10 minutes: only an invalidation can change it

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = USER_NOTIFICATION.Create(userId, "it.created", "Title", "Body", null, new FixedClock(DateTime.UtcNow));
        db.UserNotifications().Add(notification);
        await db.SaveChangesAsync();

        await WaitUntilAsync(
            async () => await counter.GetAsync(userId, CancellationToken.None) == 1,
            "the creation event to invalidate the cached count");

        await WaitUntilAsync(
            async () =>
            {
                await using var check = pipeline.Provider.CreateAsyncScope();
                var stored = await check.ServiceProvider.GetRequiredService<AppDbContext>()
                    .UserNotifications().AsNoTracking().SingleAsync(n => n.Id == notification.Id);
                return stored.PublishedAtUtc is not null;
            },
            "the notification to be marked as published");
    }

    [Fact]
    public async Task InApp_OldUnpublishedNotification_IsRetiredWithoutAnEvent()
    {
        var pipeline = await StartPipelineAsync();
        var userId = Guid.NewGuid();
        var old = USER_NOTIFICATION.Create(userId, "it.old", "Title", "Body", null, new FixedClock(DateTime.UtcNow.AddDays(-3)));

        await using (var scope = pipeline.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserNotifications().Add(old);
            await db.SaveChangesAsync();
        }

        await WaitUntilAsync(
            async () =>
            {
                await using var check = pipeline.Provider.CreateAsyncScope();
                var stored = await check.ServiceProvider.GetRequiredService<AppDbContext>()
                    .UserNotifications().AsNoTracking().SingleAsync(n => n.Id == old.Id);
                return stored.PublishedAtUtc is not null;
            },
            "the stale notification to be retired");

        var events = _kafka.ReadAll(pipeline.Topics.InApp, TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(events, e => e.Message.Value.Contains(old.Id.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}
