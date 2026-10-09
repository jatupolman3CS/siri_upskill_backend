namespace Siri.Integrations.Messaging;

/// <summary>
/// Publishes one record to a topic and completes only when the broker has durably acknowledged it (all in-sync replicas,
/// idempotent producer). A module depends on this interface — never on <c>Confluent.Kafka</c> types — so the pipeline's
/// logic stays unit-testable with a fake and the client library stays an implementation detail of this project.
/// </summary>
public interface IMessageProducer
{
    /// <summary>
    /// Sends <paramref name="value"/> keyed by <paramref name="key"/> (records with the same key land on the same partition and
    /// are consumed in order). The current <see cref="System.Diagnostics.Activity"/>'s W3C <c>traceparent</c> is attached as a
    /// header automatically so a consumer can continue the trace.
    /// </summary>
    /// <exception cref="MessagePublishException">The broker did not acknowledge the record (unreachable, timed out, rejected).</exception>
    Task ProduceAsync(
        string topic,
        string key,
        string value,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken);
}

/// <summary>A record could not be durably published. Carries the topic and a client-level reason, never the payload.</summary>
public sealed class MessagePublishException(string topic, string reason, Exception? innerException = null)
    : Exception($"Could not publish to topic '{topic}': {reason}", innerException)
{
    public string Topic { get; } = topic;
}
