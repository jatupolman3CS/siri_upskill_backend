using Siri.Modules.Identity.Domain;

namespace Siri.UnitTests.Identity;

public class UserTests
{
    [Fact]
    public void Register_ValidInput_ReturnsUserInPendingEmailConfirmationStatus()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");

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
        Assert.Throws<ArgumentException>(() => USER.Register(email, normalizedEmail, passwordHash, displayName));
    }

    [Fact]
    public void ConfirmEmail_PendingUser_TransitionsToActiveAndSetsEmailConfirmedAtUtc()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        var clock = new FakeClock(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));

        user.ConfirmEmail(clock);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(clock.UtcNow, user.EmailConfirmedAtUtc);
    }

    [Fact]
    public void ConfirmEmail_AlreadyActiveUser_ThrowsInvalidOperationException()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        var clock = new FakeClock(DateTime.UtcNow);
        user.ConfirmEmail(clock);

        Assert.Throws<InvalidOperationException>(() => user.ConfirmEmail(clock));
    }

    [Fact]
    public void Suspend_ActiveUser_SetsStatusSuspended()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        user.ConfirmEmail(new FakeClock(DateTime.UtcNow));

        user.Suspend("Repeated ToS violations");

        Assert.Equal(UserStatus.Suspended, user.Status);
    }

    [Fact]
    public void Reactivate_SuspendedUser_SetsStatusActive()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        user.ConfirmEmail(new FakeClock(DateTime.UtcNow));
        user.Suspend("Repeated ToS violations");

        user.Reactivate();

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void RecordLogin_SetsLastLoginAtUtcFromClock()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        var clock = new FakeClock(new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc));

        user.RecordLogin(clock);

        Assert.Equal(clock.UtcNow, user.LastLoginAtUtc);
    }

    [Fact]
    public void AssignRole_NewRole_AddsToRolesOnce()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        var role = new ROLE(ROLE.LearnerId, "Learner");

        user.AssignRole(role);
        user.AssignRole(role); // idempotent — assigning twice must not duplicate

        Assert.Single(user.Roles);
        Assert.Contains(user.Roles, r => r.Id == ROLE.LearnerId);
    }

    [Fact]
    public void RemoveRole_AssignedRole_RemovesIt()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        var role = new ROLE(ROLE.LearnerId, "Learner");
        user.AssignRole(role);

        user.RemoveRole(role);

        Assert.Empty(user.Roles);
    }

    [Fact]
    public void MaxConcurrentSessionsOverride_NewUser_DefaultsToNull()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");

        Assert.Null(user.MaxConcurrentSessionsOverride);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(100)]
    public void SetMaxConcurrentSessionsOverride_PositiveValue_SetsProperty(int value)
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");

        user.SetMaxConcurrentSessionsOverride(value);

        Assert.Equal(value, user.MaxConcurrentSessionsOverride);
    }

    [Fact]
    public void SetMaxConcurrentSessionsOverride_Null_ClearsAnyPreviousOverride()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
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
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");

        Assert.Throws<ArgumentOutOfRangeException>(() => user.SetMaxConcurrentSessionsOverride(value));
    }

    [Fact]
    public void ChangePassword_ActiveUser_ReplacesPasswordHash()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "old-hash", "Student One");
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
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "old-hash", "Student One");

        user.ChangePassword("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
    }

    [Fact]
    public void ChangePassword_MissingHash_ThrowsArgumentException()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "old-hash", "Student One");

        Assert.Throws<ArgumentException>(() => user.ChangePassword(""));
    }

    [Fact]
    public void ChangePassword_DeletedUser_ThrowsInvalidOperationException()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "old-hash", "Student One");
        user.Delete();

        Assert.Throws<InvalidOperationException>(() => user.ChangePassword("new-hash"));
        Assert.Equal("old-hash", user.PasswordHash); // rejected attempt must not mutate state
    }

    // ---- SetAvatarIfMissing (Google `picture` claim) -------------------------------------------

    private static USER ActiveUser()
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", "Student One");
        user.ConfirmEmail(new FakeClock(DateTime.UtcNow));
        return user;
    }

    [Fact]
    public void SetAvatarIfMissing_NoAvatarAndHttpsUrl_SetsItAndReturnsTrue()
    {
        var user = ActiveUser();

        var applied = user.SetAvatarIfMissing("https://lh3.googleusercontent.com/a/abc123=s96-c");

        Assert.True(applied);
        Assert.Equal("https://lh3.googleusercontent.com/a/abc123=s96-c", user.AvatarUrl);
    }

    [Fact]
    public void SetAvatarIfMissing_SurroundingWhitespace_IsTrimmedBeforeStoring()
    {
        var user = ActiveUser();

        var applied = user.SetAvatarIfMissing("  https://example.com/me.png \n");

        Assert.True(applied);
        Assert.Equal("https://example.com/me.png", user.AvatarUrl);
    }

    [Fact]
    public void SetAvatarIfMissing_ExistingAvatar_IsNeverOverwritten()
    {
        var user = ActiveUser();
        user.SetAvatarIfMissing("https://example.com/first.png");

        var applied = user.SetAvatarIfMissing("https://example.com/second.png");

        Assert.False(applied);
        Assert.Equal("https://example.com/first.png", user.AvatarUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://example.com/me.png")] // plain http — mixed content / spoofable in transit
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("ftp://example.com/me.png")]
    [InlineData("//example.com/me.png")] // protocol-relative, not absolute
    [InlineData("/avatars/me.png")] // relative path
    [InlineData("not a url")]
    [InlineData("https://example.com/my pic.png")] // embedded whitespace
    [InlineData("https://user:secret@example.com/me.png")] // embedded credentials
    public void SetAvatarIfMissing_UnusableCandidate_IsIgnoredAndLeavesAvatarNull(string? candidate)
    {
        var user = ActiveUser();

        var applied = user.SetAvatarIfMissing(candidate);

        Assert.False(applied);
        Assert.Null(user.AvatarUrl);
    }

    [Fact]
    public void SetAvatarIfMissing_UrlExactlyAtColumnWidth_IsAccepted()
    {
        var user = ActiveUser();
        var prefix = "https://example.com/";
        var url = prefix + new string('a', USER.AvatarUrlMaxLength - prefix.Length);
        Assert.Equal(USER.AvatarUrlMaxLength, url.Length);

        var applied = user.SetAvatarIfMissing(url);

        Assert.True(applied);
        Assert.Equal(url, user.AvatarUrl);
    }

    [Fact]
    public void SetAvatarIfMissing_UrlOneCharacterOverColumnWidth_IsIgnored()
    {
        var user = ActiveUser();
        var prefix = "https://example.com/";
        var url = prefix + new string('a', USER.AvatarUrlMaxLength - prefix.Length + 1);

        var applied = user.SetAvatarIfMissing(url);

        Assert.False(applied);
        Assert.Null(user.AvatarUrl);
    }

    [Fact]
    public void SetAvatarIfMissing_DeletedAccount_NeverGetsPersonalDataWrittenBack()
    {
        var user = ActiveUser();
        user.Anonymize("anonymized@deleted.example", "Deleted USER", "unmatchable-hash");

        var applied = user.SetAvatarIfMissing("https://example.com/me.png");

        Assert.False(applied);
        Assert.Null(user.AvatarUrl);
    }

    [Fact]
    public void SetAvatarIfMissing_AfterAnonymize_StaysNullEvenForAUserWhoHadAnAvatar()
    {
        var user = ActiveUser();
        user.SetAvatarIfMissing("https://example.com/me.png");
        user.Anonymize("anonymized@deleted.example", "Deleted USER", "unmatchable-hash");

        Assert.Null(user.AvatarUrl); // Anonymize scrubs it...
        Assert.False(user.SetAvatarIfMissing("https://example.com/me.png")); // ...and it cannot come back
    }
}
