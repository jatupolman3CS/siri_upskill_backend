using Siri.Modules.Identity.Infrastructure;

namespace Siri.UnitTests.Identity;

public class SecurityTokenGeneratorTests
{
    private readonly SecurityTokenGenerator _generator = new();

    [Fact]
    public void Generate_ReturnsRawTokenAndItsHashTogether()
    {
        var (rawToken, tokenHash) = _generator.Generate();

        Assert.NotEmpty(rawToken);
        Assert.NotEmpty(tokenHash);
        Assert.NotEqual(rawToken, tokenHash); // never the same value — the hash must not just echo the raw token
        Assert.Equal(_generator.Hash(rawToken), tokenHash); // Hash(raw) must reproduce the same hash Generate() paired with it
    }

    [Fact]
    public void Generate_CalledTwice_ProducesDifferentRawTokensAndHashes()
    {
        var first = _generator.Generate();
        var second = _generator.Generate();

        Assert.NotEqual(first.RawToken, second.RawToken);
        Assert.NotEqual(first.TokenHash, second.TokenHash);
    }

    [Fact]
    public void Hash_SameRawTokenTwice_ProducesTheSameHash()
    {
        var (rawToken, _) = _generator.Generate();

        var hash1 = _generator.Hash(rawToken);
        var hash2 = _generator.Hash(rawToken);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_DifferentRawTokens_ProduceDifferentHashes()
    {
        var hashOfA = _generator.Hash("token-a");
        var hashOfB = _generator.Hash("token-b");

        Assert.NotEqual(hashOfA, hashOfB);
    }

    [Fact]
    public void Hash_EmptyToken_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _generator.Hash(""));
    }
}
