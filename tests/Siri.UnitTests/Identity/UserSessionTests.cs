using Siri.Modules.Identity.Domain;

namespace Siri.UnitTests.Identity;

public class UserSessionTests
{
    [Fact]
    public void Start_ValidInput_CreatesActiveSession()
    {
        var userId = Guid.NewGuid();
        var clock = new FakeClock(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));

        var session = USER_SESSION.Start(userId, "device-123", "Chrome on Windows", "Mozilla/5.0", "203.0.113.10", clock);

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(userId, session.UserId);
        Assert.Equal("device-123", session.DeviceId);
        Assert.Equal(clock.UtcNow, session.CreatedAtUtc);
        Assert.Equal(clock.UtcNow, session.LastSeenAtUtc);
        Assert.Null(session.RevokedAtUtc);
        Assert.True(session.IsActive);
    }

    [Fact]
    public void Revoke_ActiveSession_SetsRevokedAtUtcAndReasonAndIsActiveFalse()
    {
        var session = USER_SESSION.Start(Guid.NewGuid(), "device-123", null, null, null, new FakeClock(DateTime.UtcNow));
        var revokeClock = new FakeClock(new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc));

        session.Revoke("Concurrent session limit exceeded", revokeClock);

        Assert.Equal(revokeClock.UtcNow, session.RevokedAtUtc);
        Assert.Equal("Concurrent session limit exceeded", session.RevokeReason);
        Assert.False(session.IsActive);
    }

    [Fact]
    public void Revoke_AlreadyRevokedSession_IsIdempotentAndKeepsOriginalRevocation()
    {
        var session = USER_SESSION.Start(Guid.NewGuid(), "device-123", null, null, null, new FakeClock(DateTime.UtcNow));
        var firstRevoke = new FakeClock(new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc));
        session.Revoke("Logout", firstRevoke);

        session.Revoke("Second revoke attempt", new FakeClock(new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(firstRevoke.UtcNow, session.RevokedAtUtc);
        Assert.Equal("Logout", session.RevokeReason);
    }

    [Fact]
    public void Touch_ActiveSession_UpdatesLastSeenAtUtc()
    {
        var startClock = new FakeClock(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));
        var session = USER_SESSION.Start(Guid.NewGuid(), "device-123", null, null, null, startClock);
        var touchClock = new FakeClock(new DateTime(2026, 8, 17, 8, 15, 0, DateTimeKind.Utc));

        session.Touch(touchClock);

        Assert.Equal(touchClock.UtcNow, session.LastSeenAtUtc);
    }

    [Fact]
    public void Touch_RevokedSession_DoesNotChangeLastSeenAtUtc()
    {
        var startClock = new FakeClock(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));
        var session = USER_SESSION.Start(Guid.NewGuid(), "device-123", null, null, null, startClock);
        session.Revoke("Logout", new FakeClock(new DateTime(2026, 8, 17, 8, 10, 0, DateTimeKind.Utc)));

        session.Touch(new FakeClock(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(startClock.UtcNow, session.LastSeenAtUtc); // unchanged — revoked sessions no longer accrue activity
    }
}
