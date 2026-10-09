using Siri.Modules.Notification.Infrastructure.Delivery;

namespace Siri.UnitTests.Notification;

public class EmailOutboxRelayStoreUnitTests
{
    [Fact]
    public void TruncateToMilliseconds_DropsTheSubMillisecondTicks()
    {
        var value = new DateTime(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc).AddTicks(TimeSpan.TicksPerMillisecond * 123 + 4567);

        var truncated = EmailOutboxRelayStore.TruncateToMilliseconds(value);

        Assert.Equal(new DateTime(2026, 10, 9, 3, 0, 0, 123, DateTimeKind.Utc), truncated);
    }

    [Fact]
    public void TruncateToMilliseconds_AlreadyWholeMilliseconds_IsUnchanged()
    {
        var value = new DateTime(2026, 10, 9, 3, 0, 0, 500, DateTimeKind.Utc);

        Assert.Equal(value, EmailOutboxRelayStore.TruncateToMilliseconds(value));
    }

    [Fact]
    public void TruncateToMilliseconds_KeepsTheUtcKindSoNpgsqlAcceptsIt()
    {
        var truncated = EmailOutboxRelayStore.TruncateToMilliseconds(DateTime.UtcNow);

        Assert.Equal(DateTimeKind.Utc, truncated.Kind);
    }

    [Fact]
    public void StampClock_AlwaysReportsTheSameInstant()
    {
        var instant = new DateTime(2026, 10, 9, 3, 0, 0, 1, DateTimeKind.Utc);
        var clock = new StampClock(instant);

        Assert.Equal(instant, clock.UtcNow);
        Assert.Equal(instant, clock.UtcNow);
    }
}
