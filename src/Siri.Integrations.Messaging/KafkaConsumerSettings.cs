namespace Siri.Integrations.Messaging;

/// <summary>Per-consumer settings (topic, group, failure policy). Connection settings live in <see cref="KafkaOptions"/>.</summary>
/// <param name="Name">Short label for logs and the client id (e.g. <c>email-delivery</c>).</param>
/// <param name="Topic">The single topic this consumer reads.</param>
/// <param name="GroupId">Consumer group; instances sharing it split the topic's partitions between them.</param>
/// <param name="DeadLetterTopic">Where records that cannot be processed are parked (with the reason). Never auto-consumed.</param>
public sealed record KafkaConsumerSettings(string Name, string Topic, string GroupId, string DeadLetterTopic)
{
    /// <summary>How many times one record is attempted (with backoff) before it is dead-lettered.</summary>
    public int MaxInPlaceAttempts { get; init; } = 5;

    /// <summary>First retry delay; doubles each attempt up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(30);
}
