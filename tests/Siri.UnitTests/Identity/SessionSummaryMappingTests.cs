using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.ListSessions;

namespace Siri.UnitTests.Identity;

/// <summary>Pure unit tests for <see cref="UserSessionMappingExtensions.ToSummary"/> — specifically the
/// "is this the current session" flag computation, isolable and testable without a database (same
/// domain-entity-construction style <c>UserSessionTests</c> already uses).</summary>
public class SessionSummaryMappingTests
{
    [Fact]
    public void ToSummary_CurrentSessionIdMatchesSessionId_IsCurrentSessionTrue()
    {
        var clock = new FakeClock(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));
        var session = USER_SESSION.Start(Guid.NewGuid(), "device-1", "My Laptop", "Mozilla/5.0", "203.0.113.10", clock);

        var summary = session.ToSummary(session.Id);

        Assert.True(summary.IsCurrentSession);
        Assert.Equal(session.Id, summary.SessionId);
        Assert.Equal("My Laptop", summary.DeviceName);
        Assert.Equal("Mozilla/5.0", summary.UserAgent);
        Assert.Equal("203.0.113.10", summary.IpAddress);
        Assert.True(summary.IsActive);
        Assert.Null(summary.RevokedAtUtc);
    }

    [Fact]
    public void ToSummary_CurrentSessionIdIsADifferentSession_IsCurrentSessionFalse()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var session = USER_SESSION.Start(Guid.NewGuid(), "device-1", null, null, null, clock);

        var summary = session.ToSummary(Guid.NewGuid());

        Assert.False(summary.IsCurrentSession);
    }

    [Fact]
    public void ToSummary_CurrentSessionIdIsNull_IsCurrentSessionFalse()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var session = USER_SESSION.Start(Guid.NewGuid(), "device-1", null, null, null, clock);

        var summary = session.ToSummary(null);

        Assert.False(summary.IsCurrentSession);
    }

    [Fact]
    public void ToSummary_RevokedSession_ReflectsRevokedStateAndTimestamp()
    {
        var clock = new FakeClock(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));
        var session = USER_SESSION.Start(Guid.NewGuid(), "device-1", null, null, null, clock);
        var revokeClock = new FakeClock(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc));
        session.Revoke("revoked_by_user", revokeClock);

        var summary = session.ToSummary(null);

        Assert.False(summary.IsActive);
        Assert.Equal(revokeClock.UtcNow, summary.RevokedAtUtc);
    }
}
