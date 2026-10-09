namespace Siri.Integrations.Messaging;

/// <summary>One record handed to an <see cref="IMessageHandler"/>. Immutable, free of Kafka client types.</summary>
public sealed record ConsumedMessage(
    string Topic,
    int Partition,
    long Offset,
    string Key,
    string Value,
    IReadOnlyDictionary<string, string> Headers,
    DateTime TimestampUtc);

/// <summary>
/// Processes one consumed record. Resolved from a fresh DI scope per record, so it may depend on scoped services such as
/// <c>AppDbContext</c>.
/// <para>
/// Contract: return normally = the record is handled (done, or intentionally skipped) and its offset may be committed.
/// Throw <see cref="PoisonMessageException"/> = this record can never succeed (malformed payload) → dead-letter it now.
/// Throw anything else = a transient failure (DB/Redis down) → the consumer retries the same record with backoff and only
/// dead-letters it after <see cref="KafkaConsumerSettings.MaxInPlaceAttempts"/> attempts. An expected business failure (an SMTP
/// rejection) is not an exception — handle it and return, recording the outcome in the system of record.
/// </para>
/// </summary>
public interface IMessageHandler
{
    Task HandleAsync(ConsumedMessage message, CancellationToken cancellationToken);
}

/// <summary>The record is permanently unprocessable; retrying would loop forever. Dead-letter it and move on.</summary>
public sealed class PoisonMessageException(string message, Exception? innerException = null)
    : Exception(message, innerException);
