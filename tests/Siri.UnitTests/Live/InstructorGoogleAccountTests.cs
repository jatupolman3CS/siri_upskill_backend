using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>Unit tests for <see cref="INSTRUCTOR_GOOGLE_ACCOUNT"/> (docs/contracts/
/// P11-03-live-module-google-meetings.md §2.1).</summary>
public class InstructorGoogleAccountTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private static INSTRUCTOR_GOOGLE_ACCOUNT Connected(FakeClock? clock = null) =>
        INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            Guid.NewGuid(), "sub-123", "teacher@example.test", "enc-refresh-1", "openid email calendar.events.owned", clock ?? new FakeClock(Now));

    [Fact]
    public void Connect_ValidInput_StartsActiveWithConnectionStamp()
    {
        var userId = Guid.NewGuid();

        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            userId, "sub-123", "teacher@example.test", "enc-refresh-1", "openid email", new FakeClock(Now));

        Assert.NotEqual(Guid.Empty, account.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
        Assert.Equal(userId, account.INSTRUCTOR_USER_ID);
        Assert.Equal("sub-123", account.GOOGLE_SUBJECT);
        Assert.Equal("teacher@example.test", account.GOOGLE_EMAIL);
        Assert.Equal("enc-refresh-1", account.REFRESH_TOKEN_ENCRYPTED);
        Assert.Equal("openid email", account.SCOPES);
        Assert.Equal(Now, account.CONNECTED_AT_UTC);
        Assert.Null(account.LAST_VALIDATED_AT_UTC);
        Assert.Null(account.REVOKED_AT_UTC);
        Assert.Null(account.REVOKED_REASON);
        Assert.True(account.IsActive);
    }

    [Fact]
    public void Connect_EmptyInstructorId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            INSTRUCTOR_GOOGLE_ACCOUNT.Connect(Guid.Empty, "sub", "a@b.test", "enc", "openid", new FakeClock(Now)));
    }

    [Theory]
    [InlineData("", "a@b.test", "enc", "openid")]
    [InlineData("sub", " ", "enc", "openid")]
    [InlineData("sub", "a@b.test", "", "openid")]
    [InlineData("sub", "a@b.test", "enc", "")]
    public void Connect_BlankRequiredField_Throws(string subject, string email, string token, string scopes)
    {
        Assert.Throws<ArgumentException>(() =>
            INSTRUCTOR_GOOGLE_ACCOUNT.Connect(Guid.NewGuid(), subject, email, token, scopes, new FakeClock(Now)));
    }

    [Fact]
    public void Connect_FieldsLongerThanColumns_Throw()
    {
        Assert.Throws<ArgumentException>(() =>
            INSTRUCTOR_GOOGLE_ACCOUNT.Connect(Guid.NewGuid(), new string('s', 65), "a@b.test", "enc", "openid", new FakeClock(Now)));
        Assert.Throws<ArgumentException>(() =>
            INSTRUCTOR_GOOGLE_ACCOUNT.Connect(Guid.NewGuid(), "sub", new string('e', 321), "enc", "openid", new FakeClock(Now)));
        Assert.Throws<ArgumentException>(() =>
            INSTRUCTOR_GOOGLE_ACCOUNT.Connect(Guid.NewGuid(), "sub", "a@b.test", "enc", new string('x', 501), new FakeClock(Now)));
    }

    [Fact]
    public void MarkValidated_StampsTime()
    {
        var clock = new FakeClock(Now);
        var account = Connected(clock);
        clock.UtcNow = Now.AddHours(1);

        account.MarkValidated(clock);

        Assert.Equal(Now.AddHours(1), account.LAST_VALIDATED_AT_UTC);
        Assert.True(account.IsActive);
    }

    [Theory]
    [InlineData(GoogleAccountRevokedReason.InvalidGrant)]
    [InlineData(GoogleAccountRevokedReason.UserDisconnected)]
    [InlineData(GoogleAccountRevokedReason.ScopeMissing)]
    [InlineData(GoogleAccountRevokedReason.InsufficientScope)]
    public void MarkRevoked_KnownReason_ClearsTokenAndRecordsWhy(string reason)
    {
        var clock = new FakeClock(Now);
        var account = Connected(clock);
        clock.UtcNow = Now.AddDays(1);

        account.MarkRevoked(reason, clock);

        Assert.Null(account.REFRESH_TOKEN_ENCRYPTED);
        Assert.Equal(Now.AddDays(1), account.REVOKED_AT_UTC);
        Assert.Equal(reason, account.REVOKED_REASON);
        Assert.False(account.IsActive);
    }

    [Fact]
    public void MarkRevoked_UnknownReason_Throws()
    {
        var account = Connected();

        Assert.Throws<ArgumentException>(() => account.MarkRevoked("because", new FakeClock(Now)));
        Assert.True(account.IsActive);
    }

    [Fact]
    public void MarkRevoked_AlreadyRevoked_KeepsOriginalReasonAndTime()
    {
        var clock = new FakeClock(Now);
        var account = Connected(clock);
        account.MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, clock);
        clock.UtcNow = Now.AddDays(2);

        account.MarkRevoked(GoogleAccountRevokedReason.UserDisconnected, clock);

        Assert.Equal(GoogleAccountRevokedReason.InvalidGrant, account.REVOKED_REASON);
        Assert.Equal(Now, account.REVOKED_AT_UTC);
    }

    [Fact]
    public void Reconnect_AfterRevocation_RestoresActiveAndClearsRevocationState()
    {
        var clock = new FakeClock(Now);
        var account = Connected(clock);
        account.MarkValidated(clock);
        account.MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, clock);
        clock.UtcNow = Now.AddDays(7);

        account.Reconnect("sub-456", "other@example.test", "enc-refresh-2", "openid email calendar.events", clock);

        Assert.True(account.IsActive);
        Assert.Equal("sub-456", account.GOOGLE_SUBJECT);
        Assert.Equal("other@example.test", account.GOOGLE_EMAIL);
        Assert.Equal("enc-refresh-2", account.REFRESH_TOKEN_ENCRYPTED);
        Assert.Equal("openid email calendar.events", account.SCOPES);
        Assert.Equal(Now.AddDays(7), account.CONNECTED_AT_UTC);
        Assert.Null(account.REVOKED_AT_UTC);
        Assert.Null(account.REVOKED_REASON);
        Assert.Null(account.LAST_VALIDATED_AT_UTC);
    }

    [Fact]
    public void Reconnect_BlankToken_ThrowsAndLeavesAccountUntouched()
    {
        var clock = new FakeClock(Now);
        var account = Connected(clock);
        account.MarkRevoked(GoogleAccountRevokedReason.UserDisconnected, clock);

        Assert.Throws<ArgumentException>(() => account.Reconnect("sub", "a@b.test", " ", "openid", clock));

        Assert.False(account.IsActive);
        Assert.Equal(GoogleAccountRevokedReason.UserDisconnected, account.REVOKED_REASON);
    }
}
