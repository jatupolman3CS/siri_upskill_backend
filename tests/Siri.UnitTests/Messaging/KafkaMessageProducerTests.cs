using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Messaging;

namespace Siri.UnitTests.Messaging;

public class KafkaMessageProducerTests
{
    private static KafkaMessageProducer ProducerWith(Action<KafkaOptions> configure)
    {
        var options = new KafkaOptions { BootstrapServers = "127.0.0.1:1", ProduceTimeoutSeconds = 1 };
        configure(options);
        return new KafkaMessageProducer(Options.Create(options), NullLogger<KafkaMessageProducer>.Instance);
    }

    [Fact]
    public async Task ProduceAsync_ProducerCannotBeBuilt_SurfacesAsMessagePublishException()
    {
        // A CA file that does not exist makes the native client refuse to start. Whatever type of exception that is, callers must see ONE kind
        // of failure to handle — otherwise the relay's "release the claimed rows" path would be skipped.
        using var producer = ProducerWith(o =>
        {
            o.SecurityProtocol = SecurityProtocol.Ssl;
            o.SslCaLocation = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.pem");
        });

        var failure = await Assert.ThrowsAsync<MessagePublishException>(() =>
            producer.ProduceAsync("topic", "key", "value", headers: null, CancellationToken.None));

        Assert.Equal("topic", failure.Topic);
    }

    [Fact]
    public async Task ProduceAsync_ConstructionFailureIsNotCached_SoEveryCallReportsItAgainInsteadOfPoisoningTheProcess()
    {
        using var producer = ProducerWith(o =>
        {
            o.SecurityProtocol = SecurityProtocol.Ssl;
            o.SslCaLocation = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.pem");
        });

        var first = await Assert.ThrowsAsync<MessagePublishException>(() => producer.ProduceAsync("t", "k", "v", null, CancellationToken.None));
        var second = await Assert.ThrowsAsync<MessagePublishException>(() => producer.ProduceAsync("t", "k", "v", null, CancellationToken.None));

        // Two distinct failures: the second call tried to build the producer again (and would succeed the moment the cause went away).
        Assert.NotSame(first, second);
        Assert.NotNull(first.InnerException);
        Assert.NotSame(first.InnerException, second.InnerException);
    }

    [Fact]
    public async Task ProduceAsync_BrokerUnreachable_TimesOutAsMessagePublishExceptionWithinTheConfiguredBound()
    {
        using var producer = ProducerWith(_ => { });

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<MessagePublishException>(() => producer.ProduceAsync("topic", "key", "value", null, CancellationToken.None));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15), "a produce must fail within a bounded time when the broker is down");
    }

    [Fact]
    public async Task ProduceAsync_AfterDispose_Fails()
    {
        var producer = ProducerWith(_ => { });
        producer.Dispose();

        await Assert.ThrowsAsync<MessagePublishException>(() => producer.ProduceAsync("topic", "key", "value", null, CancellationToken.None));
    }

    [Fact]
    public void Dispose_NeverUsed_DoesNotCreateAProducerOrThrow()
    {
        var producer = ProducerWith(_ => { });

        producer.Dispose();
        producer.Dispose(); // idempotent
    }

    [Fact]
    public async Task ProduceAsync_BlankTopic_IsAProgrammerErrorNotAPublishFailure()
    {
        using var producer = ProducerWith(_ => { });

        await Assert.ThrowsAsync<ArgumentException>(() => producer.ProduceAsync(" ", "key", "value", null, CancellationToken.None));
    }
}
