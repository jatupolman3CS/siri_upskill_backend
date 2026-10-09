using System.Text.Json;
using Siri.Integrations.Messaging;

namespace Siri.UnitTests.Messaging;

public class DeadLetterEnvelopeTests
{
    [Fact]
    public void ToJson_ThenFromJson_RoundTripsEveryField()
    {
        var failedAt = new DateTime(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc);
        var envelope = new DeadLetterEnvelope("siri.notification.email.v1", 2, 41, "k", "boom", 5, failedAt, "{\"messageId\":\"x\"}");

        var parsed = DeadLetterEnvelope.FromJson(envelope.ToJson());

        Assert.Equal(envelope, parsed);
    }

    [Fact]
    public void ToJson_UsesCamelCaseNames()
    {
        var json = new DeadLetterEnvelope("t", 0, 0, "k", "r", 1, DateTime.UnixEpoch, "v").ToJson();

        Assert.Contains("\"sourceTopic\"", json, StringComparison.Ordinal);
        Assert.Contains("\"originalValue\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FromJson_Null_Throws()
    {
        Assert.Throws<JsonException>(() => DeadLetterEnvelope.FromJson("null"));
    }
}
