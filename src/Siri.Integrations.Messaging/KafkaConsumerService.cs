using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Siri.Integrations.Messaging;

/// <summary>
/// A hosted consumer that reads one topic as part of a consumer group and hands each record to <typeparamref name="THandler"/>
/// (resolved from a new DI scope per record).
/// <para>
/// <b>Delivery guarantee: at-least-once.</b> Offsets are stored only <i>after</i> the handler returned (or the record was
/// dead-lettered) and committed to the broker in the background (<c>enable.auto.offset.store=false</c>,
/// <c>enable.auto.commit=true</c> — the pattern librdkafka documents for at-least-once). A crash between handling and the next
/// commit redelivers the record, so handlers must be idempotent.
/// </para>
/// <para>
/// <b>Failure policy.</b> A transient exception retries the same record in place with exponential backoff (a partition is
/// ordered, so skipping ahead would break ordering); after <see cref="KafkaConsumerSettings.MaxInPlaceAttempts"/> — or at once for a
/// <see cref="PoisonMessageException"/> — the record is written to the dead-letter topic and the consumer moves on, so one bad
/// record can never wedge a partition. If even the dead-letter write fails, the offset is <b>not</b> stored and the consumer
/// restarts: a record is never dropped silently.
/// </para>
/// </summary>
public sealed class KafkaConsumerService<THandler> : BackgroundService
    where THandler : class, IMessageHandler
{
    /// <summary>How long the loop sleeps (asynchronously) when a non-blocking poll found nothing. The latency this adds to an idle consumer is at most this much.</summary>
    private static readonly TimeSpan IdlePollDelay = TimeSpan.FromMilliseconds(25);

    private static readonly TimeSpan ErrorPollDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    private readonly KafkaConsumerSettings _settings;
    private readonly KafkaOptions _kafkaOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageProducer _producer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<KafkaConsumerService<THandler>> _logger;
    private readonly string _instanceSuffix;

    public KafkaConsumerService(
        KafkaConsumerSettings settings,
        string instanceSuffix,
        IOptions<KafkaOptions> kafkaOptions,
        IServiceScopeFactory scopeFactory,
        IMessageProducer producer,
        TimeProvider timeProvider,
        ILogger<KafkaConsumerService<THandler>> logger)
    {
        _settings = settings;
        _instanceSuffix = instanceSuffix;
        _kafkaOptions = kafkaOptions.Value;
        _scopeFactory = scopeFactory;
        _producer = producer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't hold up host startup: BackgroundService.StartAsync returns once this method hits its first await.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Kafka consumer {Consumer} stopped unexpectedly; restarting in {DelaySeconds}s",
                    _settings.Name,
                    RestartDelay.TotalSeconds);

                try
                {
                    await Task.Delay(RestartDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var config = KafkaClientConfig.Apply(new ConsumerConfig(), _kafkaOptions, $"{_settings.Name}-{_instanceSuffix}");
        config.GroupId = _settings.GroupId;
        config.AutoOffsetReset = AutoOffsetReset.Earliest;
        config.EnableAutoCommit = true;
        config.EnableAutoOffsetStore = false;
        config.AutoCommitIntervalMs = 5_000;
        config.AllowAutoCreateTopics = false;
        config.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;
        config.SessionTimeoutMs = 30_000;
        // Handling one record can legitimately wait (rate-limit back-off, SMTP); this is the longest silence the group tolerates.
        config.MaxPollIntervalMs = 300_000;

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
                _logger.LogWarning("Kafka consumer {Consumer} reported {Code}: {Reason}", _settings.Name, error.Code, error.Reason))
            .SetPartitionsAssignedHandler((_, partitions) =>
                _logger.LogInformation("Kafka consumer {Consumer} assigned {Partitions}", _settings.Name, string.Join(",", partitions.Select(p => p.Partition.Value))))
            .SetPartitionsRevokedHandler((_, partitions) =>
                _logger.LogInformation("Kafka consumer {Consumer} revoked {Partitions}", _settings.Name, string.Join(",", partitions.Select(p => p.Partition.Value))))
            .Build();

        consumer.Subscribe(_settings.Topic);
        _logger.LogInformation(
            "Kafka consumer {Consumer} subscribed to {Topic} in group {GroupId}",
            _settings.Name,
            _settings.Topic,
            _settings.GroupId);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    // Non-blocking poll (zero timeout): returns at once with a record or nothing, so no thread-pool thread is ever parked inside the
                    // client waiting for data — the loop sleeps with an await instead. (librdkafka fetches into its own queue in the background.)
                    result = consumer.Consume(TimeSpan.Zero);
                }
                catch (ConsumeException ex) when (!ex.Error.IsFatal)
                {
                    _logger.LogWarning("Kafka consumer {Consumer} consume error {Code}: {Reason}", _settings.Name, ex.Error.Code, ex.Error.Reason);

                    // Typically "unknown topic" while the provisioner is still creating it, or a broker blip: without this pause a persistent
                    // error would be reported on every poll, in a tight loop.
                    await Task.Delay(ErrorPollDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (result?.Message is null)
                {
                    // Nothing waiting (or an end-of-partition marker): rest briefly instead of spinning.
                    await Task.Delay(IdlePollDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await ProcessAsync(ToConsumedMessage(result), cancellationToken).ConfigureAwait(false);

                // Only now — handled or dead-lettered — may this offset be committed.
                consumer.StoreOffset(result);
            }
        }
        finally
        {
            // Leaves the group cleanly (commits stored offsets, triggers an immediate rebalance for the other instances).
            consumer.Close();
        }
    }

    private async Task ProcessAsync(ConsumedMessage message, CancellationToken cancellationToken)
    {
        using var activity = StartActivity(message);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var scope = _scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var handler = scope.ServiceProvider.GetRequiredService<THandler>();
                    await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
                }

                return;
            }
            catch (PoisonMessageException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Kafka consumer {Consumer} dead-lettering unprocessable record {Topic}[{Partition}]@{Offset}",
                    _settings.Name,
                    message.Topic,
                    message.Partition,
                    message.Offset);
                await DeadLetterAsync(message, ex.Message, attempt, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= _settings.MaxInPlaceAttempts)
                {
                    _logger.LogError(
                        ex,
                        "Kafka consumer {Consumer} gave up on {Topic}[{Partition}]@{Offset} after {Attempts} attempts; dead-lettering",
                        _settings.Name,
                        message.Topic,
                        message.Partition,
                        message.Offset,
                        attempt);
                    // The type only: exception messages can carry hosts, connection details or values, and this topic is readable by anyone with access to the
                    // broker. The full exception is in the log line above.
                    await DeadLetterAsync(message, ex.GetType().Name, attempt, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var delay = RetryBackoff.For(attempt, _settings.RetryBaseDelay, _settings.MaxRetryDelay);
                _logger.LogWarning(
                    ex,
                    "Kafka consumer {Consumer} failed on {Topic}[{Partition}]@{Offset} (attempt {Attempt}/{Max}); retrying in {DelaySeconds}s",
                    _settings.Name,
                    message.Topic,
                    message.Partition,
                    message.Offset,
                    attempt,
                    _settings.MaxInPlaceAttempts,
                    delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DeadLetterAsync(ConsumedMessage message, string reason, int attempts, CancellationToken cancellationToken)
    {
        var envelope = new DeadLetterEnvelope(
            message.Topic,
            message.Partition,
            message.Offset,
            message.Key,
            reason.Length > 500 ? reason[..500] : reason,
            attempts,
            _timeProvider.GetUtcNow().UtcDateTime,
            message.Value);

        await _producer
            .ProduceAsync(_settings.DeadLetterTopic, message.Key, envelope.ToJson(), headers: null, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Activity? StartActivity(ConsumedMessage message)
    {
        ActivityContext parent = default;
        if (message.Headers.TryGetValue(KafkaMessageProducer.TraceParentHeader, out var traceParent))
        {
            ActivityContext.TryParse(traceParent, traceState: null, out parent);
        }

        return MessagingTelemetry.ActivitySource.StartActivity(
            $"{message.Topic} process",
            ActivityKind.Consumer,
            parent);
    }

    private static ConsumedMessage ToConsumedMessage(ConsumeResult<string, string> result)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in result.Message.Headers)
        {
            headers[header.Key] = Encoding.UTF8.GetString(header.GetValueBytes());
        }

        return new ConsumedMessage(
            result.Topic,
            result.Partition.Value,
            result.Offset.Value,
            result.Message.Key ?? string.Empty,
            result.Message.Value ?? string.Empty,
            headers,
            result.Message.Timestamp.UtcDateTime);
    }
}
