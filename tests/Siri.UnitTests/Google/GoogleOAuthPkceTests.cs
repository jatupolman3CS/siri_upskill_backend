using Siri.Integrations.Google;

namespace Siri.UnitTests.Google;

public sealed class GoogleOAuthPkceTests
{
    [Fact]
    public void ComputeCodeChallenge_MatchesTheRfc7636AppendixBVector()
    {
        // RFC 7636 appendix B: the canonical verifier -> S256 challenge pair.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", GoogleOAuthPkce.ComputeCodeChallenge(verifier));
    }

    [Fact]
    public void GenerateCodeVerifier_Is64UnreservedCharactersAndNeverRepeats()
    {
        var verifiers = Enumerable.Range(0, 50).Select(_ => GoogleOAuthPkce.GenerateCodeVerifier()).ToArray();

        Assert.All(verifiers, verifier =>
        {
            Assert.Equal(64, verifier.Length);
            Assert.Matches("^[A-Za-z0-9._~-]{64}$", verifier);
        });
        Assert.Equal(verifiers.Length, verifiers.Distinct().Count());
    }

    [Fact]
    public void GenerateState_Is32RandomBytesAsBase64UrlWithoutPadding()
    {
        var states = Enumerable.Range(0, 50).Select(_ => GoogleOAuthPkce.GenerateState()).ToArray();

        Assert.All(states, state =>
        {
            Assert.Equal(43, state.Length); // 32 bytes -> 43 base64url chars, no '=' padding
            Assert.Matches("^[A-Za-z0-9_-]{43}$", state);
        });
        Assert.Equal(states.Length, states.Distinct().Count());
    }

    [Fact]
    public void ComputeCodeChallenge_OfGeneratedVerifier_IsBase64UrlOfSha256()
    {
        var challenge = GoogleOAuthPkce.ComputeCodeChallenge(GoogleOAuthPkce.GenerateCodeVerifier());

        Assert.Matches("^[A-Za-z0-9_-]{43}$", challenge);
    }

    [Fact]
    public void HashState_IsStableLowercaseSha256Hex_AndDoesNotEchoTheState()
    {
        var hash = GoogleOAuthPkce.HashState("some-state-value");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, GoogleOAuthPkce.HashState("some-state-value"));
        Assert.NotEqual(hash, GoogleOAuthPkce.HashState("some-state-value2"));
        Assert.DoesNotContain("some-state-value", hash);
    }

    [Fact]
    public void Helpers_RejectEmptyInput()
    {
        Assert.Throws<ArgumentException>(() => GoogleOAuthPkce.ComputeCodeChallenge(string.Empty));
        Assert.Throws<ArgumentException>(() => GoogleOAuthPkce.HashState(string.Empty));
    }
}
