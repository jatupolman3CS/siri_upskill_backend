using System.Text.Json;

namespace Siri.Integrations.Messaging;

/// <summary>
/// What is written to a dead-letter topic: the original record plus why it ended up there and where it came from, so an
/// operator can inspect or replay it. The original value is carried verbatim (the producers in this system put no secrets or
/// message bodies in their values — see the claim-check payloads in the Notification module).
/// </summary>
public sealed record DeadLetterEnvelope(
    string SourceTopic,
    int Partition,
    long Offset,
    string Key,
    string Reason,
    int Attempts,
    DateTime FailedAtUtc,
    string OriginalValue)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static DeadLetterEnvelope FromJson(string json) =>
        JsonSerializer.Deserialize<DeadLetterEnvelope>(json, JsonOptions)
        ?? throw new JsonException("Dead-letter envelope was empty.");
}
