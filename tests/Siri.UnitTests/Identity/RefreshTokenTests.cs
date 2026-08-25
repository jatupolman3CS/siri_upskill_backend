using Siri.Modules.Identity.Domain;

namespace Siri.UnitTests.Identity;

public class RefreshTokenTests
{
    [Fact]
    public void Issue_ValidInput_CreatesUnrevokedToken()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var expiresAtUtc = new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);

        var token = RefreshToken.Issue(userId, sessionId, "sha256-hash-of-raw-token", expiresAtUtc);

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.Equal(userId, token.UserId);
        Assert.Equal(sessionId, token.SessionId);
        Assert.Equal("sha256-hash-of-raw-token", token.TokenHash);
        Assert.Equal(expiresAtUtc, token.ExpiresAtUtc);
        Assert.Null(token.RevokedAtUtc);
        Assert.Null(token.ReplacedByTokenId);
    }

    [Fact]
    public void Revoke_SetsRevokedAtUtcAndReplacedByTokenId()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "old-hash", DateTime.UtcNow.AddDays(30));
        var replacementId = Guid.NewGuid();
        var clock = new FakeClock(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc));

        token.Revoke(replacementId, clock);

        Assert.Equal(clock.UtcNow, token.RevokedAtUtc);
        Assert.Equal(replacementId, token.ReplacedByTokenId);
    }

    [Fact]
    public void Revoke_WithoutReplacement_SetsRevokedAtUtcAndLeavesReplacedByTokenIdNull()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "old-hash", DateTime.UtcNow.AddDays(30));

        token.Revoke(null, new FakeClock(DateTime.UtcNow));

        Assert.NotNull(token.RevokedAtUtc);
        Assert.Null(token.ReplacedByTokenId);
    }
}
