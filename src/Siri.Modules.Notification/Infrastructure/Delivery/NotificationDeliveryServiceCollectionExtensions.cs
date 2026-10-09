using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Siri.Integrations.Messaging;

namespace Siri.Modules.Notification.Infrastructure.Delivery;

public static class NotificationDeliveryServiceCollectionExtensions
{
    /// <summary>
    /// Starts the Kafka notification pipeline in this host: the two outbox relays (email, in-app), the email-delivery consumers and
    /// the in-app event consumer, plus the topics they use. Call it from the <b>worker</b> host only, after
    /// <c>AddNotificationModule</c> — the API host only writes outbox rows and must not run these loops.
    /// <para>
    /// A no-op unless <c>Notification:Delivery:Transport</c> is <c>Kafka</c>, so the default deployment is unchanged: the Hangfire
    /// <c>email-outbox-send</c> job keeps delivering and no <c>Kafka:*</c> configuration is required. (The transport is read from
    /// configuration here, at registration time, because it decides which services exist at all; so are the topic prefix and the
    /// replication factor, which fix the topic definitions. Their values are validated at host start.)
    /// </para>
    /// </summary>
    public static IServiceCollection AddNotificationDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(NotificationDeliveryOptions.SectionName).Get<NotificationDeliveryOptions>()
            ?? new NotificationDeliveryOptions();

        if (options.Transport != NotificationTransport.Kafka)
        {
            return services;
        }

        services.AddKafkaMessaging(configuration);

        var kafka = configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>() ?? new KafkaOptions();
        var topics = NotificationTopics.For(string.IsNullOrWhiteSpace(kafka.TopicPrefix) ? new KafkaOptions().TopicPrefix : kafka.TopicPrefix);
        services.AddSingleton(topics);

        var retention = TimeSpan.FromDays(options.TopicRetentionDays);
        foreach (var topic in new[] { topics.Email, topics.InApp })
        {
            services.AddKafkaTopic(new KafkaTopicDefinition(topic, options.Partitions, kafka.ReplicationFactor, retention));
        }

        // Dead-letter topics: small, operator-read, kept longer than the work topics so a weekend incident is still inspectable.
        foreach (var topic in new[] { topics.EmailDeadLetter, topics.InAppDeadLetter })
        {
            services.AddKafkaTopic(new KafkaTopicDefinition(topic, 1, kafka.ReplicationFactor, retention * 4));
        }

        // Consumer progress marker the email relay consults before it re-claims rows that merely wait in Kafka (see IEmailConsumerHeartbeat).
        services.AddSingleton<IEmailConsumerHeartbeat, RedisEmailConsumerHeartbeat>();

        // Relays: claim outbox rows → publish.
        services.AddScoped<IEmailOutboxRelayStore, EmailOutboxRelayStore>();
        services.AddScoped<IInAppRelayStore, InAppRelayStore>();
        services.AddSingleton<IHostedService, EmailOutboxRelay>();
        services.AddSingleton<IHostedService, InAppNotificationRelay>();

        // Consumers: topic → effect.
        services.AddScoped<InAppNotificationHandler>();
        services.AddKafkaConsumer<EmailDeliveryHandler>(
            new KafkaConsumerSettings("email-delivery", topics.Email, topics.EmailGroup, topics.EmailDeadLetter),
            options.EmailConsumerInstances);
        services.AddKafkaConsumer<InAppNotificationHandler>(
            new KafkaConsumerSettings("inapp-events", topics.InApp, topics.InAppGroup, topics.InAppDeadLetter));

        return services;
    }
}

/// <summary>What the notification pipeline needs configured before a host may start in Production. Shared so every host that runs
/// the pipeline checks the same things (same shape as <c>EmailProductionRequirements</c>).</summary>
public static class NotificationDeliveryProductionRequirements
{
    /// <summary>Human-readable problems (never including secret values); empty when acceptable — and always empty for the database transport.</summary>
    public static IReadOnlyList<string> GetProblems(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(NotificationDeliveryOptions.SectionName).Get<NotificationDeliveryOptions>()
            ?? new NotificationDeliveryOptions();

        return options.Transport == NotificationTransport.Kafka
            ? KafkaProductionRequirements.GetProblems(configuration)
            : [];
    }
}
