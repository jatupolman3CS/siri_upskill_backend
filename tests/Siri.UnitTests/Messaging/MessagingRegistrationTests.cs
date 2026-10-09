using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Siri.Integrations.Messaging;

namespace Siri.UnitTests.Messaging;

public class MessagingRegistrationTests
{
    private sealed class NoopHandler : IMessageHandler
    {
        public Task HandleAsync(ConsumedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static ServiceProvider Build(string? bootstrapServers, Action<IServiceCollection>? configure = null)
    {
        var values = new Dictionary<string, string?>();
        if (bootstrapServers is not null)
        {
            values["Kafka:BootstrapServers"] = bootstrapServers;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKafkaMessaging(configuration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddKafkaMessaging_ValidConfiguration_ResolvesTheProducerWithoutConnecting()
    {
        using var provider = Build("localhost:1");

        // Resolving must not open a connection (the underlying producer is created lazily on first publish), so a host that has the
        // pipeline registered but a broker that is down still starts.
        Assert.NotNull(provider.GetRequiredService<IMessageProducer>());
    }

    [Fact]
    public void AddKafkaMessaging_MissingBootstrapServers_FailsWhenOptionsAreRead()
    {
        using var provider = Build(bootstrapServers: null);

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<KafkaOptions>>().Value);

        Assert.Contains("BootstrapServers", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddKafkaMessaging_CalledTwice_RegistersOneTopicProvisioner()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kafka:BootstrapServers"] = "localhost:1" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKafkaMessaging(configuration);
        services.AddKafkaMessaging(configuration);

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(KafkaTopicProvisioner));
    }

    [Fact]
    public void AddKafkaConsumer_WithTwoInstances_RegistersTwoHostedConsumers()
    {
        using var provider = Build("localhost:1", services =>
        {
            services.AddScoped<NoopHandler>();
            services.AddKafkaConsumer<NoopHandler>(new KafkaConsumerSettings("noop", "topic", "group", "topic.dlq"), instances: 2);
        });

        var consumers = provider.GetServices<IHostedService>().OfType<KafkaConsumerService<NoopHandler>>().ToList();

        Assert.Equal(2, consumers.Count);
    }

    [Fact]
    public void AddKafkaConsumer_ZeroInstances_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddKafkaConsumer<NoopHandler>(new KafkaConsumerSettings("noop", "topic", "group", "topic.dlq"), instances: 0));
    }
}
