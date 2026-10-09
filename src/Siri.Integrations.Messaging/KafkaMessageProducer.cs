using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Siri.Integrations.Messaging;

/// <summary>
/// The one Kafka producer of the process (singleton — <c>librdkafka</c> producers are expensive and meant to be shared).
/// Reliability settings are deliberate and not configurable: <c>enable.idempotence</c> (no duplicates or reordering from
/// client retries, implies <c>acks=all</c>), a bounded <c>message.timeout.ms</c> so an outage surfaces as a
/// <see cref="MessagePublishException"/> instead of hanging a relay cycle forever, and a tiny <c>linger</c> that batches the
/// records of one relay cycle without adding noticeable latency.
/// <para>
/// <b>Every</b> failure to publish — a broker refusal, a timeout, and also a producer that cannot even be built (bad TLS file, missing native
/// library) — surfaces as <see cref="MessagePublishException"/>, so callers have one thing to handle. The underlying producer is created on
/// first use and <b>re-created after a fatal client error</b> (idempotent producers can enter a state they never leave) or after a failed
/// construction: a transient problem must not poison the process until it restarts.
/// </para>
/// </summary>
internal sealed class KafkaMessageProducer : IMessageProducer, IDisposable
{
    internal const string TraceParentHeader = "traceparent";

    private readonly ProducerConfig _config;
    private readonly ILogger<KafkaMessageProducer> _logger;
    private readonly object _gate = new();
    private IProducer<string, string>? _producer;
    private bool _disposed;

    public KafkaMessageProducer(IOptions<KafkaOptions> options, ILogger<KafkaMessageProducer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;

        var kafkaOptions = options.Value;
        _config = KafkaClientConfig.Apply(new ProducerConfig(), kafkaOptions, "producer");
        _config.EnableIdempotence = true;
        _config.Acks = Acks.All;
        _config.MessageTimeoutMs = kafkaOptions.ProduceTimeoutSeconds * 1000;
        _config.LingerMs = 5;
        // Topics are created by KafkaTopicProvisioner with an explicit partition count; a produce to a topic that does not exist yet must
        // fail (and be retried) rather than make the broker invent one with its default shape.
        _config.AllowAutoCreateTopics = false;
    }

    public async Task ProduceAsync(
        string topic,
        string key,
        string value,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var message = new Message<string, string> { Key = key, Value = value, Headers = BuildHeaders(headers) };

        IProducer<string, string> producer;
        try
        {
            producer = GetOrCreateProducer();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not cached: the next call tries again (the cause may be a file that appears later, or a library that gets fixed by a redeploy).
            throw new MessagePublishException(topic, $"the Kafka producer could not be created ({ex.GetType().Name})", ex);
        }

        try
        {
            await producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
        }
        catch (ProduceException<string, string> ex)
        {
            DiscardIfFatal(producer, ex.Error);
            throw new MessagePublishException(topic, ex.Error.Reason, ex);
        }
        catch (KafkaException ex)
        {
            DiscardIfFatal(producer, ex.Error);
            throw new MessagePublishException(topic, ex.Error.Reason, ex);
        }
    }

    private IProducer<string, string> GetOrCreateProducer()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _producer ??= new ProducerBuilder<string, string>(_config)
                .SetErrorHandler((_, error) =>
                {
                    // Transient broker/connection errors are retried by the client; fatal ones surface on the next produce.
                    _logger.LogWarning("Kafka producer reported {Code}: {Reason}", error.Code, error.Reason);
                })
                .Build();
        }
    }

    /// <summary>A fatal error leaves the client unusable for good; drop it so the next publish builds a fresh one instead of failing until restart.</summary>
    private void DiscardIfFatal(IProducer<string, string> failed, Error error)
    {
        if (!error.IsFatal)
        {
            return;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_producer, failed))
            {
                return; // someone else already replaced it
            }

            _producer = null;
        }

        _logger.LogError("Kafka producer hit a fatal error ({Code}: {Reason}); it will be re-created on the next publish", error.Code, error.Reason);

        try
        {
            failed.Dispose();
        }
        catch (Exception ex) when (ex is KafkaException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Disposing the failed Kafka producer raised an error; ignored");
        }
    }

    private static Headers BuildHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        var result = new Headers();

        if (headers is not null)
        {
            foreach (var (name, headerValue) in headers)
            {
                result.Add(name, System.Text.Encoding.UTF8.GetBytes(headerValue));
            }
        }

        if (Activity.Current?.Id is { } traceParent && !(headers?.ContainsKey(TraceParentHeader) ?? false))
        {
            result.Add(TraceParentHeader, System.Text.Encoding.UTF8.GetBytes(traceParent));
        }

        return result;
    }

    public void Dispose()
    {
        IProducer<string, string>? producer;
        lock (_gate)
        {
            _disposed = true;
            producer = _producer;
            _producer = null;
        }

        if (producer is null)
        {
            return;
        }

        try
        {
            // Drain anything still buffered before the process exits.
            producer.Flush(TimeSpan.FromSeconds(5));
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex, "Kafka producer could not flush on shutdown");
        }

        producer.Dispose();
    }
}
