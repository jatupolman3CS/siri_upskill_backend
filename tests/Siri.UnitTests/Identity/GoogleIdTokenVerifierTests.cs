using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Siri.Modules.Identity.Infrastructure;

namespace Siri.UnitTests.Identity;

/// <summary>
/// Drives <see cref="GoogleIdTokenVerifier"/> against a fake Google (discovery document + JWKS served
/// from an in-memory <see cref="HttpMessageHandler"/>) with tokens signed by a locally generated RSA
/// key — the real verification code path, minus the network.
/// </summary>
public sealed class GoogleIdTokenVerifierTests
{
    private const string ClientId = "test-client-id.apps.googleusercontent.com";
    private const string Issuer = "https://accounts.google.com";

    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeGoogle : HttpMessageHandler
    {
        public readonly List<string> Requests = [];

        public RsaSecurityKey SigningKey { get; set; } = NewKey("key-1");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);

            var body = request.RequestUri.AbsolutePath switch
            {
                "/.well-known/openid-configuration" =>
                    $$"""{"issuer":"{{Issuer}}","jwks_uri":"https://fake.google.test/certs"}""",
                "/certs" => JwksJson(SigningKey),
                _ => null,
            };

            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class MutableClock(DateTime now) : Siri.SharedKernel.IClock
    {
        public DateTime UtcNow { get; set; } = now;
    }

    private static RsaSecurityKey NewKey(string kid) => new(RSA.Create(2048)) { KeyId = kid };

    private static string JwksJson(RsaSecurityKey key)
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
        return $$"""{"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{jwk.Kid}}","n":"{{jwk.N}}","e":"{{jwk.E}}"}]}""";
    }

    private static GoogleIdTokenVerifier CreateVerifier(FakeGoogle google, MutableClock clock, string clientId = ClientId) =>
        new(
            new FakeHttpClientFactory(google),
            Options.Create(new GoogleLoginOptions
            {
                ClientId = clientId,
                DiscoveryUrl = "https://fake.google.test/.well-known/openid-configuration",
            }),
            clock,
            NullLogger<GoogleIdTokenVerifier>.Instance);

    private static string Token(
        RsaSecurityKey key,
        string issuer = Issuer,
        string audience = ClientId,
        DateTime? expires = null,
        object? emailVerified = null,
        string subject = "google-sub-1",
        string email = "person@example.com",
        string? picture = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = Now.AddMinutes(-1),
            NotBefore = Now.AddMinutes(-1),
            Expires = expires ?? Now.AddMinutes(30),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["email"] = email,
                ["email_verified"] = emailVerified ?? true,
                ["name"] = "Person One",
            },
        };

        if (picture is not null)
        {
            descriptor.Claims["picture"] = picture;
        }

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsIdentity()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));

        var identity = await verifier.VerifyAsync(Token(google.SigningKey), CancellationToken.None);

        Assert.NotNull(identity);
        Assert.Equal("google-sub-1", identity.Subject);
        Assert.Equal("person@example.com", identity.Email);
        Assert.True(identity.EmailVerified);
        Assert.Equal("Person One", identity.Name);
        Assert.Null(identity.Picture); // the claim is optional — absent means null, not an error
    }

    [Fact]
    public async Task VerifyAsync_TokenWithPictureClaim_ExposesItOnTheIdentity()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));

        var identity = await verifier.VerifyAsync(
            Token(google.SigningKey, picture: "https://lh3.googleusercontent.com/a/abc123=s96-c"),
            CancellationToken.None);

        Assert.NotNull(identity);
        Assert.Equal("https://lh3.googleusercontent.com/a/abc123=s96-c", identity.Picture);
    }

    [Fact]
    public async Task VerifyAsync_EmailVerifiedAsString_IsReadAsBoolean()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));

        var verified = await verifier.VerifyAsync(Token(google.SigningKey, emailVerified: "true"), CancellationToken.None);
        var unverified = await verifier.VerifyAsync(Token(google.SigningKey, emailVerified: "false"), CancellationToken.None);

        Assert.True(verified!.EmailVerified);
        Assert.False(unverified!.EmailVerified);
    }

    [Fact]
    public async Task VerifyAsync_AlternateGoogleIssuerSpelling_IsAccepted()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));

        var identity = await verifier.VerifyAsync(Token(google.SigningKey, issuer: "accounts.google.com"), CancellationToken.None);

        Assert.NotNull(identity);
    }

    [Fact]
    public async Task VerifyAsync_TokenIssuedToAnotherClient_IsRejected()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));

        var identity = await verifier.VerifyAsync(Token(google.SigningKey, audience: "someone-elses.apps.googleusercontent.com"), CancellationToken.None);

        Assert.Null(identity);
    }

    [Fact]
    public async Task VerifyAsync_WrongIssuer_IsRejected()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));

        var identity = await verifier.VerifyAsync(Token(google.SigningKey, issuer: "https://evil.example.com"), CancellationToken.None);

        Assert.Null(identity);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredToken_IsRejected()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));

        var identity = await verifier.VerifyAsync(Token(google.SigningKey, expires: Now.AddMinutes(-10)), CancellationToken.None);

        Assert.Null(identity);
    }

    [Fact]
    public async Task VerifyAsync_TokenSignedByUnknownKey_IsRejected()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));
        var attackerKey = NewKey("key-1"); // same kid as Google's key, different key material

        var identity = await verifier.VerifyAsync(Token(attackerKey), CancellationToken.None);

        Assert.Null(identity);
    }

    [Fact]
    public async Task VerifyAsync_UnsignedToken_IsRejected()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));
        var unsigned = new JsonWebTokenHandler().CreateToken(
            $$"""{"iss":"{{Issuer}}","aud":"{{ClientId}}","sub":"x","email":"a@b.com","email_verified":true,"exp":{{new DateTimeOffset(Now.AddMinutes(30)).ToUnixTimeSeconds()}}}""");

        var identity = await verifier.VerifyAsync(unsigned, CancellationToken.None);

        Assert.Null(identity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task VerifyAsync_MalformedToken_IsRejected(string token)
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));

        var identity = await verifier.VerifyAsync(token, CancellationToken.None);

        Assert.Null(identity);
    }

    [Fact]
    public async Task VerifyAsync_ClientIdNotConfigured_RejectsEverythingWithoutCallingGoogle()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now), clientId: "");

        var identity = await verifier.VerifyAsync(Token(google.SigningKey), CancellationToken.None);

        Assert.Null(identity);
        Assert.Empty(google.Requests);
    }

    [Fact]
    public async Task VerifyAsync_KeysAreCachedBetweenCalls()
    {
        var google = new FakeGoogle();
        var verifier = CreateVerifier(google, new MutableClock(Now));

        await verifier.VerifyAsync(Token(google.SigningKey), CancellationToken.None);
        await verifier.VerifyAsync(Token(google.SigningKey), CancellationToken.None);

        Assert.Equal(2, google.Requests.Count); // one discovery + one JWKS, not per call
    }

    [Fact]
    public async Task VerifyAsync_GoogleRotatesItsKey_RefetchesAndAcceptsTheNewKey()
    {
        var google = new FakeGoogle();
        var clock = new MutableClock(Now);
        var verifier = CreateVerifier(google, clock);

        Assert.NotNull(await verifier.VerifyAsync(Token(google.SigningKey), CancellationToken.None));

        google.SigningKey = NewKey("key-2");
        clock.UtcNow = Now.AddMinutes(5); // past the minimum refresh interval, still inside the cache lifetime

        var identity = await verifier.VerifyAsync(Token(google.SigningKey), CancellationToken.None);

        Assert.NotNull(identity);
    }

    [Fact]
    public async Task VerifyAsync_GoogleUnreachable_ReturnsNullInsteadOfThrowing()
    {
        var verifier = new GoogleIdTokenVerifier(
            new FakeHttpClientFactory(new FailingHandler()),
            Options.Create(new GoogleLoginOptions { ClientId = ClientId }),
            new MutableClock(Now),
            NullLogger<GoogleIdTokenVerifier>.Instance);

        var identity = await verifier.VerifyAsync(Token(NewKey("key-1")), CancellationToken.None);

        Assert.Null(identity);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("network down");
    }
}

public sealed class GoogleLoginOptionsValidatorTests
{
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string discoveryUrl) =>
        new GoogleLoginOptionsValidator().Validate(null, new GoogleLoginOptions { DiscoveryUrl = discoveryUrl });

    [Fact]
    public void Validate_DefaultGoogleUrl_Succeeds() =>
        Assert.True(Validate(GoogleLoginOptions.DefaultDiscoveryUrl).Succeeded);

    [Fact]
    public void Validate_LocalhostHttp_Succeeds() =>
        Assert.True(Validate("http://localhost:9099/.well-known/openid-configuration").Succeeded);

    [Fact]
    public void Validate_RemoteHttp_Fails() =>
        Assert.True(Validate("http://accounts.example.com/.well-known/openid-configuration").Failed);

    [Fact]
    public void Validate_NotAUrl_Fails() =>
        Assert.True(Validate("not a url").Failed);

    [Fact]
    public void IsEnabled_FollowsClientId()
    {
        Assert.False(new GoogleLoginOptions().IsEnabled);
        Assert.True(new GoogleLoginOptions { ClientId = "x.apps.googleusercontent.com" }.IsEnabled);
    }
}
