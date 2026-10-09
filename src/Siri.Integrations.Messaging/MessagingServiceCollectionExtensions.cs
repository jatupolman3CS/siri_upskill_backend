using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Siri.Integrations.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Kafka connection options (validated on start), the shared <see cref="IMessageProducer"/> singleton and the
    /// <see cref="KafkaTopicProvisioner"/>. Call it only from a host that runs a Kafka pipeline — a host that does not use Kafka
    /// must not need any <c>Kafka:*</c> configuration. Safe to call more than once.
    /// </summary>
    public static IServiceCollection AddKafkaMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaOptions>()
            .Bind(configuration.GetSection(KafkaOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaOptions>, KafkaOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMessageProducer, KafkaMessageProducer>();

        // One provisioner for every module's topics (they register KafkaTopicDefinition singletons).
        if (!services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(KafkaTopicProvisioner)))
        {
            services.AddSingleton<IHostedService, KafkaTopicProvisioner>();
        }

        return services;
    }

    /// <summary>Declares a topic that must exist; created by <see cref="KafkaTopicProvisioner"/> when missing.</summary>
    public static IServiceCollection AddKafkaTopic(this IServiceCollection services, KafkaTopicDefinition topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        services.AddSingleton(topic);
        return services;
    }

    /// <summary>
    /// Runs <paramref name="instances"/> consumers of <typeparamref name="THandler"/> in the same consumer group (they split the
    /// topic's partitions; more instances than partitions just idle). <typeparamref name="THandler"/> is resolved per record from
    /// its own scope and must be registered by the caller (usually scoped).
    /// </summary>
    public static IServiceCollection AddKafkaConsumer<THandler>(
        this IServiceCollection services,
        KafkaConsumerSettings settings,
        int instances = 1)
        where THandler : class, IMessageHandler
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(instances, 1);

        for (var i = 1; i <= instances; i++)
        {
            var suffix = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            services.AddSingleton<IHostedService>(sp => new KafkaConsumerService<THandler>(
                settings,
                suffix,
                sp.GetRequiredService<IOptions<KafkaOptions>>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IMessageProducer>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<KafkaConsumerService<THandler>>>()));
        }

        return services;
    }
}
