using Siri.Modules.Identity.Domain;

namespace Siri.UnitTests.Identity;

public class UserTests
{
    [Fact]
    public void Register_ValidInput_ReturnsUserInPendingEmailConfirmationStatus()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");

        Assert.Equal(UserStatus.PendingEmailConfirmation, user.Status);
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("student@example.com", user.Email);
        Assert.Equal("STUDENT@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("hashed-password", user.PasswordHash);
        Assert.Equal("Student One", user.DisplayName);
        Assert.Null(user.EmailConfirmedAtUtc);
        Assert.False(user.TwoFactorEnabled);
        Assert.Empty(user.Roles);
    }

    [Theory]
    [InlineData("", "N@X.COM", "hash", "Name")]
    [InlineData("e@x.com", "", "hash", "Name")]
    [InlineData("e@x.com", "E@X.COM", "", "Name")]
    [InlineData("e@x.com", "E@X.COM", "hash", "")]
    public void Register_MissingRequiredField_ThrowsArgumentException(
        string email, string normalizedEmail, string passwordHash, string displayName)
    {
        Assert.Throws<ArgumentException>(() => User.Register(email, normalizedEmail, passwordHash, displayName));
    }

    [Fact]
    public void ConfirmEmail_PendingUser_TransitionsToActiveAndSetsEmailConfirmedAtUtc()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        var clock = new FakeClock(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));

        user.ConfirmEmail(clock);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(clock.UtcNow, user.EmailConfirmedAtUtc);
    }

    [Fact]
    public void ConfirmEmail_AlreadyActiveUser_ThrowsInvalidOperationException()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        var clock = new FakeClock(DateTime.UtcNow);
        user.ConfirmEmail(clock);

        Assert.Throws<InvalidOperationException>(() => user.ConfirmEmail(clock));
    }

    [Fact]
    public void Suspend_ActiveUser_SetsStatusSuspended()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        user.ConfirmEmail(new FakeClock(DateTime.UtcNow));

        user.Suspend("Repeated ToS violations");

        Assert.Equal(UserStatus.Suspended, user.Status);
    }

    [Fact]
    public void Reactivate_SuspendedUser_SetsStatusActive()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        user.ConfirmEmail(new FakeClock(DateTime.UtcNow));
        user.Suspend("Repeated ToS violations");

        user.Reactivate();

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void RecordLogin_SetsLastLoginAtUtcFromClock()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        var clock = new FakeClock(new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc));

        user.RecordLogin(clock);

        Assert.Equal(clock.UtcNow, user.LastLoginAtUtc);
    }

    [Fact]
    public void AssignRole_NewRole_AddsToRolesOnce()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        var role = new Role(Role.LearnerId, "Learner");

        user.AssignRole(role);
        user.AssignRole(role); // idempotent — assigning twice must not duplicate

        Assert.Single(user.Roles);
        Assert.Contains(user.Roles, r => r.Id == Role.LearnerId);
    }

    [Fact]
    public void RemoveRole_AssignedRole_RemovesIt()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        var role = new Role(Role.LearnerId, "Learner");
        user.AssignRole(role);

        user.RemoveRole(role);

        Assert.Empty(user.Roles);
    }

    [Fact]
    public void MaxConcurrentSessionsOverride_NewUser_DefaultsToNull()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");

        Assert.Null(user.MaxConcurrentSessionsOverride);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(100)]
    public void SetMaxConcurrentSessionsOverride_PositiveValue_SetsProperty(int value)
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");

        user.SetMaxConcurrentSessionsOverride(value);

        Assert.Equal(value, user.MaxConcurrentSessionsOverride);
    }

    [Fact]
    public void SetMaxConcurrentSessionsOverride_Null_ClearsAnyPreviousOverride()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        user.SetMaxConcurrentSessionsOverride(5);

        user.SetMaxConcurrentSessionsOverride(null);

        Assert.Null(user.MaxConcurrentSessionsOverride);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void SetMaxConcurrentSessionsOverride_ZeroOrNegative_ThrowsArgumentOutOfRangeException(int value)
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");

        Assert.Throws<ArgumentOutOfRangeException>(() => user.SetMaxConcurrentSessionsOverride(value));
    }

    [Fact]
    public void ChangePassword_ActiveUser_ReplacesPasswordHash()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "old-hash", "Student One");
        user.ConfirmEmail(new FakeClock(DateTime.UtcNow));

        user.ChangePassword("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
    }

    [Fact]
    public void ChangePassword_PendingEmailConfirmationUser_ReplacesPasswordHash()
    {
        // ChangePassword itself does not restrict by status beyond "not Deleted" — see its own doc
        // comment for why that specific policy choice (only Active accounts may go through a reset)
        // belongs to ResetPasswordHandler, not this domain method.
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "old-hash", "Student One");

        user.ChangePassword("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
    }

    [Fact]
    public void ChangePassword_MissingHash_ThrowsArgumentException()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "old-hash", "Student One");

        Assert.Throws<ArgumentException>(() => user.ChangePassword(""));
    }

    [Fact]
    public void ChangePassword_DeletedUser_ThrowsInvalidOperationException()
    {
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "old-hash", "Student One");
        user.Delete();

        Assert.Throws<InvalidOperationException>(() => user.ChangePassword("new-hash"));
        Assert.Equal("old-hash", user.PasswordHash); // rejected attempt must not mutate state
    }
}
