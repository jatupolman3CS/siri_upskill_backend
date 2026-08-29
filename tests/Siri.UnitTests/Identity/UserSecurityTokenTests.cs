using Siri.Modules.Identity.Domain;

namespace Siri.UnitTests.Identity;

public class UserSecurityTokenTests
{
    [Fact]
    public void Issue_ValidInput_CreatesUnconsumedToken()
    {
        var userId = Guid.NewGuid();
        var expiresAtUtc = new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc);

        var token = USER_SECURITY_TOKEN.Issue(userId, UserSecurityTokenPurpose.EmailConfirmation, "sha256-hash", expiresAtUtc);

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.Equal(userId, token.UserId);
        Assert.Equal(UserSecurityTokenPurpose.EmailConfirmation, token.Purpose);
        Assert.Equal("sha256-hash", token.TokenHash);
        Assert.Equal(expiresAtUtc, token.ExpiresAtUtc);
        Assert.Null(token.ConsumedAtUtc);
        Assert.False(token.IsConsumed);
    }

    [Fact]
    public void Issue_MissingTokenHash_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            USER_SECURITY_TOKEN.Issue(Guid.NewGuid(), UserSecurityTokenPurpose.EmailConfirmation, "", DateTime.UtcNow.AddHours(24)));
    }

    [Fact]
    public void IsExpired_ClockBeforeExpiry_ReturnsFalse()
    {
        var issuedClock = new FakeClock(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc));
        var token = USER_SECURITY_TOKEN.Issue(
            Guid.NewGuid(), UserSecurityTokenPurpose.EmailConfirmation, "hash", issuedClock.UtcNow.AddHours(24));

        var checkClock = new FakeClock(issuedClock.UtcNow.AddHours(23));

        Assert.False(token.IsExpired(checkClock));
        Assert.True(token.IsValid(checkClock));
    }

    [Fact]
    public void IsExpired_ClockAtOrAfterExpiry_ReturnsTrue()
    {
        var expiresAtUtc = new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc);
        var token = USER_SECURITY_TOKEN.Issue(Guid.NewGuid(), UserSecurityTokenPurpose.EmailConfirmation, "hash", expiresAtUtc);

        var checkClock = new FakeClock(expiresAtUtc);

        Assert.True(token.IsExpired(checkClock));
        Assert.False(token.IsValid(checkClock));
    }

    [Fact]
    public void IsValid_ConsumedToken_ReturnsFalseEvenIfNotExpired()
    {
        var clock = new FakeClock(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc));
        var token = USER_SECURITY_TOKEN.Issue(
            Guid.NewGuid(), UserSecurityTokenPurpose.EmailConfirmation, "hash", clock.UtcNow.AddHours(24));

        token.Consume(clock);

        Assert.False(token.IsValid(clock));
    }

    [Fact]
    public void Consume_ValidToken_SetsConsumedAtUtcFromClock()
    {
        var clock = new FakeClock(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc));
        var token = USER_SECURITY_TOKEN.Issue(
            Guid.NewGuid(), UserSecurityTokenPurpose.EmailConfirmation, "hash", clock.UtcNow.AddHours(24));

        token.Consume(clock);

        Assert.Equal(clock.UtcNow, token.ConsumedAtUtc);
        Assert.True(token.IsConsumed);
    }

    [Fact]
    public void Consume_AlreadyConsumedToken_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc));
        var token = USER_SECURITY_TOKEN.Issue(
            Guid.NewGuid(), UserSecurityTokenPurpose.EmailConfirmation, "hash", clock.UtcNow.AddHours(24));
        token.Consume(clock);

        Assert.Throws<InvalidOperationException>(() => token.Consume(clock));
    }

    [Fact]
    public void Consume_ExpiredToken_ThrowsInvalidOperationException()
    {
        var expiresAtUtc = new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);
        var token = USER_SECURITY_TOKEN.Issue(Guid.NewGuid(), UserSecurityTokenPurpose.EmailConfirmation, "hash", expiresAtUtc);
        var afterExpiryClock = new FakeClock(expiresAtUtc.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => token.Consume(afterExpiryClock));
        Assert.Null(token.ConsumedAtUtc); // rejected attempt must not mutate state
    }
}
