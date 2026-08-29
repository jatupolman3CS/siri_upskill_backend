using Siri.Modules.Identity.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Workers;

public sealed class DataRetentionCleanupJobTests
{
    private readonly IClock _clock;

    public DataRetentionCleanupJobTests()
    {
        _clock = new FixedClock(new DateTime(2026, 8, 27, 4, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void UserSecurityToken_IsExpired_ReturnsTrueWhenPastExpiration()
    {
        var now = _clock.UtcNow;
        var token = USER_SECURITY_TOKEN.Issue(
            Guid.NewGuid(),
            UserSecurityTokenPurpose.EmailConfirmation,
            "hash1",
            now.AddDays(-1));

        Assert.True(token.IsExpired(_clock));
        Assert.False(token.IsValid(_clock));
    }

    [Fact]
    public void UserSecurityToken_Consume_MarksConsumedAndBecomesInvalid()
    {
        var now = _clock.UtcNow;
        var token = USER_SECURITY_TOKEN.Issue(
            Guid.NewGuid(),
            UserSecurityTokenPurpose.PasswordReset,
            "hash2",
            now.AddHours(1));

        Assert.False(token.IsConsumed);
        Assert.True(token.IsValid(_clock));

        token.Consume(_clock);

        Assert.True(token.IsConsumed);
        Assert.False(token.IsValid(_clock));
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
