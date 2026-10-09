using Siri.SharedKernel;

namespace Siri.UnitTests;

/// <summary>Reading the creation time back out of a UUIDv7 key (how the e-mail outbox reports the age of its oldest waiting message without a created-at column).</summary>
public class UuidV7TimestampTests
{
    [Fact]
    public void ANewId_CarriesTheTimeItWasCreated()
    {
        var before = DateTime.UtcNow;
        var id = UuidV7.NewId();
        var after = DateTime.UtcNow;

        Assert.True(UuidV7.TryGetTimestampUtc(id, out var timestamp));
        Assert.Equal(DateTimeKind.Utc, timestamp.Kind);
        Assert.InRange(timestamp, before.AddMilliseconds(-5), after.AddMilliseconds(5));
    }

    [Fact]
    public void AKnownVersion7Value_DecodesToItsMilliseconds()
    {
        // RFC 9562: the first 48 bits are Unix milliseconds, in the order they are written in the canonical text form.
        var id = Guid.Parse("018f1a8d-ea00-7000-8000-000000000000");
        var milliseconds = 0x018F1A8DEA00L;

        Assert.True(UuidV7.TryGetTimestampUtc(id, out var timestamp));
        Assert.Equal(DateTime.UnixEpoch.AddMilliseconds(milliseconds), timestamp);
    }

    [Fact]
    public void TwoIdsMadeInOrder_DecodeInOrder()
    {
        var first = UuidV7.NewId();
        Thread.Sleep(5);
        var second = UuidV7.NewId();

        Assert.True(UuidV7.TryGetTimestampUtc(first, out var a));
        Assert.True(UuidV7.TryGetTimestampUtc(second, out var b));
        Assert.True(a < b);
    }

    [Fact]
    public void ARandomVersion4Guid_HasNoTimestamp()
    {
        Assert.False(UuidV7.TryGetTimestampUtc(Guid.NewGuid(), out var timestamp));
        Assert.Equal(default, timestamp);
    }

    [Fact]
    public void AnEmptyGuid_HasNoTimestamp() => Assert.False(UuidV7.TryGetTimestampUtc(Guid.Empty, out _));
}
