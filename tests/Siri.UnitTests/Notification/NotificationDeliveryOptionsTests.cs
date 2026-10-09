using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Siri.Integrations.Messaging;
using Siri.Modules.Notification;
using Siri.Modules.Notification.Infrastructure.Delivery;

namespace Siri.UnitTests.Notification;

public class NotificationDeliveryOptionsTests
{
    private static ValidateOptionsResult Validate(NotificationDeliveryOptions options) =>
        new NotificationDeliveryOptionsValidator().Validate(name: null, options);

    private static NotificationDeliveryOptions KafkaOptions() => new() { Transport = NotificationTransport.Kafka };

    [Fact]
    public void Defaults_UseTheDatabaseTransportSoNothingChangesUntilKafkaIsChosen()
    {
        var options = new NotificationDeliveryOptions();

        Assert.Equal(NotificationTransport.Database, options.Transport);
        Assert.Equal(0, options.MaxEmailsPerMinute);
    }

    [Fact]
    public void Validate_DatabaseTransport_AcceptsAnythingBecauseNoneOfItIsUsed()
    {
        var options = new NotificationDeliveryOptions { Partitions = 0, RelayBatchSize = 0 };

        Assert.True(Validate(options).Succeeded);
    }

    [Theory]
    [InlineData(NotificationTransport.Database)]
    [InlineData(NotificationTransport.Kafka)]
    public void Validate_StaleWindowShorterThanTheDeliveryClaim_FailsOnEveryTransport(NotificationTransport transport)
    {
        // The polling job reads this value too (it adopts rows a Kafka relay left Queued), so it is checked even when Kafka is off.
        var options = new NotificationDeliveryOptions { Transport = transport, QueuedStaleAfterMinutes = NotificationDeliveryOptionsValidator.MinimumStaleAfterMinutes - 1 };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("QueuedStaleAfterMinutes", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NotificationTransport.Database)]
    [InlineData(NotificationTransport.Kafka)]
    public void Validate_StaleWindowAtTheMinimum_Succeeds(NotificationTransport transport)
    {
        var options = new NotificationDeliveryOptions { Transport = transport, QueuedStaleAfterMinutes = NotificationDeliveryOptionsValidator.MinimumStaleAfterMinutes };

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Validate_StaleWindowMinimumOutlastsTheRedisDeliveryClaim()
    {
        Assert.True(TimeSpan.FromMinutes(NotificationDeliveryOptionsValidator.MinimumStaleAfterMinutes) > RedisEmailDeliveryGuard.ClaimLifetime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public void Validate_UnreadCountCacheOutOfRange_FailsEvenOnTheDatabaseTransport(int seconds)
    {
        var options = new NotificationDeliveryOptions { Transport = NotificationTransport.Database, UnreadCountCacheSeconds = seconds };

        Assert.True(Validate(options).Failed);
    }

    [Fact]
    public void Validate_KafkaTransportWithDefaults_Succeeds()
    {
        Assert.True(Validate(KafkaOptions()).Succeeded);
    }

    [Theory]
    [InlineData(nameof(NotificationDeliveryOptions.Partitions), 0)]
    [InlineData(nameof(NotificationDeliveryOptions.TopicRetentionDays), 0)]
    [InlineData(nameof(NotificationDeliveryOptions.EmailConsumerInstances), 0)]
    [InlineData(nameof(NotificationDeliveryOptions.EmailConsumerInstances), 33)]
    [InlineData(nameof(NotificationDeliveryOptions.RelayBatchSize), 0)]
    [InlineData(nameof(NotificationDeliveryOptions.RelayBatchSize), 1001)]
    [InlineData(nameof(NotificationDeliveryOptions.RelayPollIntervalMs), 10)]
    [InlineData(nameof(NotificationDeliveryOptions.QueuedStaleAfterMinutes), 1)]
    [InlineData(nameof(NotificationDeliveryOptions.InAppEventMaxAgeHours), 0)]
    [InlineData(nameof(NotificationDeliveryOptions.MaxEmailsPerMinute), -1)]
    [InlineData(nameof(NotificationDeliveryOptions.UnreadCountCacheSeconds), 0)]
    [InlineData(nameof(NotificationDeliveryOptions.UnreadCountCacheSeconds), 3601)]
    public void Validate_NumberOutsideItsRange_Fails(string property, int value)
    {
        var options = KafkaOptions();
        typeof(NotificationDeliveryOptions).GetProperty(property)!.SetValue(options, value);

        Assert.True(Validate(options).Failed, $"{property}={value} should be rejected");
    }

    [Fact]
    public void Binding_FromConfiguration_ReadsTheTransportAndOverrides()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notification:Delivery:Transport"] = "Kafka",
                ["Notification:Delivery:Partitions"] = "6",
                ["Notification:Delivery:MaxEmailsPerMinute"] = "90",
            })
            .Build();

        var options = configuration.GetSection(NotificationDeliveryOptions.SectionName).Get<NotificationDeliveryOptions>()!;

        Assert.Equal(NotificationTransport.Kafka, options.Transport);
        Assert.Equal(6, options.Partitions);
        Assert.Equal(90, options.MaxEmailsPerMinute);
    }

    [Fact]
    public void Durations_AreDerivedFromTheConfiguredNumbers()
    {
        var options = new NotificationDeliveryOptions { QueuedStaleAfterMinutes = 20, InAppEventMaxAgeHours = 3, RelayPollIntervalMs = 250 };

        Assert.Equal(TimeSpan.FromMinutes(20), options.QueuedStaleAfter);
        Assert.Equal(TimeSpan.FromHours(3), options.InAppEventMaxAge);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.RelayPollInterval);
    }
}

public class RelayPacingTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);

    [Fact]
    public void NextDelay_NothingRelayed_WaitsThePollInterval()
    {
        Assert.Equal(Poll, RelayPacing.NextDelay(relayed: 0, batchSize: 100, consecutiveFailures: 0, Poll));
    }

    [Fact]
    public void NextDelay_PartialBatch_WaitsThePollInterval()
    {
        Assert.Equal(Poll, RelayPacing.NextDelay(relayed: 40, batchSize: 100, consecutiveFailures: 0, Poll));
    }

    [Fact]
    public void NextDelay_FullBatch_GoesAgainImmediatelyToDrainABacklog()
    {
        Assert.Equal(TimeSpan.Zero, RelayPacing.NextDelay(relayed: 100, batchSize: 100, consecutiveFailures: 0, Poll));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(10, 30)]
    public void NextDelay_AfterFailures_BacksOffExponentiallyUpToThirtySeconds(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RelayPacing.NextDelay(relayed: 0, batchSize: 100, failures, Poll));
    }

    [Fact]
    public void NextDelay_FailuresWinOverAFullBatch()
    {
        Assert.True(RelayPacing.NextDelay(relayed: 100, batchSize: 100, consecutiveFailures: 1, Poll) > TimeSpan.Zero);
    }
}

public class SendThrottleWindowTests
{
    [Fact]
    public void WindowFor_MidMinute_KeysTheMinuteAndReportsTheRemainder()
    {
        var (key, untilNext) = RedisEmailSendThrottle.WindowFor(new DateTime(2026, 10, 9, 3, 7, 45, 500, DateTimeKind.Utc));

        Assert.Equal("notify:email:rate:202610090307", key);
        Assert.Equal(TimeSpan.FromMilliseconds(14_500), untilNext);
    }

    [Fact]
    public void WindowFor_ExactlyOnTheMinute_ASixtySecondWindowRemains()
    {
        var (key, untilNext) = RedisEmailSendThrottle.WindowFor(new DateTime(2026, 12, 31, 23, 59, 0, DateTimeKind.Utc));

        Assert.Equal("notify:email:rate:202612312359", key);
        Assert.Equal(TimeSpan.FromSeconds(60), untilNext);
    }

    [Fact]
    public void WindowFor_TwoInstantsInTheSameMinute_ShareAKey()
    {
        var first = RedisEmailSendThrottle.WindowFor(new DateTime(2026, 10, 9, 3, 7, 1, DateTimeKind.Utc)).Key;
        var second = RedisEmailSendThrottle.WindowFor(new DateTime(2026, 10, 9, 3, 7, 59, DateTimeKind.Utc)).Key;

        Assert.Equal(first, second);
    }
}

public class NotificationDeliveryRegistrationTests
{
    private static IConfiguration Config(string transport, params (string Key, string Value)[] more)
    {
        var values = new Dictionary<string, string?>
        {
            ["Notification:Delivery:Transport"] = transport,
            ["Email:Provider"] = "Log",
        };
        foreach (var (key, value) in more)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceCollection Services(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationModule(configuration);
        services.AddNotificationDelivery(configuration);
        return services;
    }

    [Fact]
    public void AddNotificationDelivery_DatabaseTransport_RegistersNoPipelineAtAll()
    {
        var services = Services(Config("Database"));

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IMessageProducer));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddNotificationDelivery_KafkaTransport_RegistersTheConsumerHeartbeatTheRelayConsults()
    {
        var services = Services(Config("Kafka", ("Kafka:BootstrapServers", "localhost:1")));

        Assert.Contains(services, d => d.ServiceType == typeof(IEmailConsumerHeartbeat));
    }

    [Fact]
    public void AddNotificationDelivery_DatabaseTransport_HasNoHeartbeatBecauseThereAreNoConsumers()
    {
        var services = Services(Config("Database"));

        // EmailDeliveryHandler takes the heartbeat as an optional dependency, so the polling transport simply runs without one.
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IEmailConsumerHeartbeat));
        Assert.Contains(services, d => d.ServiceType == typeof(EmailDeliveryHandler));
    }

    [Fact]
    public void AddNotificationDelivery_NothingConfigured_BehavesAsDatabaseTransport()
    {
        var services = Services(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Provider"] = "Log" }).Build());

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddNotificationDelivery_KafkaTransport_StartsTheRelaysTheConsumersAndTheTopicProvisioner()
    {
        var services = Services(Config("Kafka", ("Kafka:BootstrapServers", "localhost:1"), ("Notification:Delivery:EmailConsumerInstances", "2")));
        using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().Select(h => h.GetType().Name).ToList();

        Assert.Equal(1, hosted.Count(n => n == nameof(KafkaTopicProvisioner)));
        Assert.Equal(1, hosted.Count(n => n == "EmailOutboxRelay"));
        Assert.Equal(1, hosted.Count(n => n == "InAppNotificationRelay"));
        Assert.Equal(3, hosted.Count(n => n.StartsWith("KafkaConsumerService", StringComparison.Ordinal))); // 2 email + 1 in-app
    }

    [Fact]
    public void AddNotificationDelivery_KafkaTransport_DeclaresTheWorkAndDeadLetterTopics()
    {
        var services = Services(Config("Kafka", ("Kafka:BootstrapServers", "localhost:1")));
        using var provider = services.BuildServiceProvider();

        var topics = provider.GetServices<KafkaTopicDefinition>().ToDictionary(t => t.Name);
        var names = NotificationTopics.For("siriupskill");

        Assert.Equal(4, topics.Count);
        Assert.Equal(new NotificationDeliveryOptions().Partitions, topics[names.Email].Partitions);
        Assert.Equal(1, topics[names.EmailDeadLetter].Partitions);
        Assert.True(topics[names.EmailDeadLetter].Retention > topics[names.Email].Retention);
    }

    [Fact]
    public void AddNotificationDelivery_KafkaTransport_TopicsFollowTheClusterTopicPrefixAndReplicationFactor()
    {
        var services = Services(Config(
            "Kafka",
            ("Kafka:BootstrapServers", "localhost:1"),
            ("Kafka:TopicPrefix", "siriupskill-dev"),
            ("Kafka:ReplicationFactor", "3")));
        using var provider = services.BuildServiceProvider();

        var topics = provider.GetServices<KafkaTopicDefinition>().ToList();

        Assert.All(topics, t => Assert.StartsWith("siriupskill-dev.notification.", t.Name, StringComparison.Ordinal));
        Assert.All(topics, t => Assert.Equal(3, t.ReplicationFactor));
        Assert.Equal(NotificationTopics.For("siriupskill-dev"), provider.GetRequiredService<NotificationTopics>());
    }

    [Fact]
    public void NotificationModule_KafkaTransportWithoutBrokerAddress_FailsWhenKafkaOptionsAreRead()
    {
        var services = Services(Config("Kafka"));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<KafkaOptions>>().Value);

        Assert.Contains("BootstrapServers", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationModule_InvalidDeliverySettings_FailWhenOptionsAreRead()
    {
        var services = Services(Config("Kafka", ("Kafka:BootstrapServers", "localhost:1"), ("Notification:Delivery:Partitions", "0")));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NotificationDeliveryOptions>>().Value);
    }

    [Fact]
    public void ProductionRequirements_DatabaseTransport_NeverComplainsAboutKafka()
    {
        Assert.Empty(NotificationDeliveryProductionRequirements.GetProblems(Config("Database")));
    }

    [Fact]
    public void ProductionRequirements_KafkaTransportWithoutBroker_ListsTheProblems()
    {
        var problems = NotificationDeliveryProductionRequirements.GetProblems(Config("Kafka"));

        Assert.Contains(problems, p => p.Contains("BootstrapServers", StringComparison.Ordinal));
    }
}
