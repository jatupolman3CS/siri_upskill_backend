using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Admin.SuspendUser;
using Siri.Modules.Identity.Features.Admin.UpdateUserRoles;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Identity;

public sealed class AdminUserTests
{
    [Fact]
    public void Suspend_WhenActive_TransitionsToSuspended()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var user = User.Register("test@siriupskill.com", "TEST@SIRIUPSKILL.COM", "hash", "Test User");
        user.ConfirmEmail(clock);

        Assert.Equal(UserStatus.Active, user.Status);

        user.Suspend("Violation of terms");

        Assert.Equal(UserStatus.Suspended, user.Status);
    }

    [Fact]
    public void Reactivate_WhenSuspended_TransitionsToActive()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var user = User.Register("test@siriupskill.com", "TEST@SIRIUPSKILL.COM", "hash", "Test User");
        user.ConfirmEmail(clock);
        user.Suspend("Violation of terms");

        user.Reactivate();

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void Reactivate_WhenNotSuspended_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var user = User.Register("test@siriupskill.com", "TEST@SIRIUPSKILL.COM", "hash", "Test User");
        user.ConfirmEmail(clock);

        Assert.Throws<InvalidOperationException>(() => user.Reactivate());
    }

    [Fact]
    public void SecurityAudit_Record_SetsAllProperties()
    {
        var now = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var userId = Guid.NewGuid();

        var audit = SecurityAudit.Record("AdminAction", userId, "Detailed action", "127.0.0.1", clock);

        Assert.NotEqual(Guid.Empty, audit.Id);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal("AdminAction", audit.EventType);
        Assert.Equal("Detailed action", audit.Detail);
        Assert.Equal("127.0.0.1", audit.IpAddress);
        Assert.Equal(now, audit.OccurredAtUtc);
    }
}
