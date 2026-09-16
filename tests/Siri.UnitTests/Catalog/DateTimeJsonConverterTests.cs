using System.Text.Json;

namespace Siri.UnitTests.Catalog;

public sealed class DateTimeJsonConverterTests
{
    private sealed record DateTimePayload(DateTime StartsAtUtc, DateTime EndsAtUtc);

    [Fact]
    public void SystemTextJson_IsoStringWithZSuffix_ParsesAsUtcDateTimeKind()
    {
        const string json = """
            {
                "startsAtUtc": "2026-10-01T03:00:00Z",
                "endsAtUtc": "2026-10-01T05:00:00Z"
            }
            """;

        var payload = JsonSerializer.Deserialize<DateTimePayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(payload);
        Assert.Equal(DateTimeKind.Utc, payload.StartsAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, payload.EndsAtUtc.Kind);
        Assert.Equal(2026, payload.StartsAtUtc.Year);
        Assert.Equal(10, payload.StartsAtUtc.Month);
        Assert.Equal(1, payload.StartsAtUtc.Day);
        Assert.Equal(3, payload.StartsAtUtc.Hour);
    }

    [Fact]
    public void SystemTextJson_IsoStringWithOffset_DoesNotParseAsUtcDateTimeKind()
    {
        const string json = """
            {
                "startsAtUtc": "2026-10-01T10:00:00+07:00",
                "endsAtUtc": "2026-10-01T12:00:00+07:00"
            }
            """;

        var payload = JsonSerializer.Deserialize<DateTimePayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(payload);
        // An offset string is parsed by System.Text.Json as Local (or Unspecified), NOT Utc.
        Assert.NotEqual(DateTimeKind.Utc, payload.StartsAtUtc.Kind);
    }
}
