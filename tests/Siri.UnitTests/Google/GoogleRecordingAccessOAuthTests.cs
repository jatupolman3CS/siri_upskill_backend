using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Integrations.Google.Logging;

namespace Siri.UnitTests.Google;

/// <summary>P11-13: the second (recording) consent URL, the <c>hd</c> claim, and the fake OAuth service's Workspace + recording behaviour.</summary>
public sealed class GoogleRecordingAccessOAuthTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private const string ClientId = "client-id-123.apps.googleusercontent.com";
    private const string ClientSecret = "super-secret-client-value";
    private const string RedirectUri = "https://api.example.com/api/live/instructor/google/callback";

    private static GoogleOAuthOptions ConfiguredOptions() => new()
    {
        ClientId = ClientId,
        ClientSecret = ClientSecret,
        RedirectUri = RedirectUri,
    };

    private static GoogleOAuthService Create(StubHttpHandler handler, GoogleOAuthOptions? options = null) =>
        new(
            new StubHttpClientFactory(handler),
            Options.Create(options ?? ConfiguredOptions()),
            new FixedClock(Now),
            new CapturingLogger<GoogleOAuthService>());

    private static LoggingGoogleOAuthService CreateFake(string? devAccountEmail = null) =>
        new(
            Options.Create(new GoogleOAuthOptions { RedirectUri = "http://localhost:5190/api/live/instructor/google/callback", DevAccountEmail = devAccountEmail ?? string.Empty }),
            new FixedClock(Now),
            new CapturingLogger<LoggingGoogleOAuthService>());

    private static string IdToken(object payload)
    {
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("""{"alg":"RS256"}"""u8.ToArray())}.{B64(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload)))}.signature";
    }

    // ---- BuildRecordingAccessAuthorizationUrl -----------------------------------------------------------------

    [Fact]
    public void BuildRecordingAccessAuthorizationUrl_AddsBothRecordingScopesAndIncludesGrantedScopes()
    {
        var url = Create(new StubHttpHandler()).BuildRecordingAccessAuthorizationUrl("state-abc", "challenge-xyz");

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal(
            $"openid email {GoogleScopes.CalendarEventsOwned} {GoogleScopes.MeetSpaceReadonly} {GoogleScopes.DriveMeetReadonly}",
            query["scope"]);
        Assert.Equal("true", query["include_granted_scopes"]);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal(RedirectUri, query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("state-abc", query["state"]);
        Assert.Equal("challenge-xyz", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal(10, query.Count);
        Assert.DoesNotContain(ClientSecret, url);
    }

    [Fact]
    public void BuildRecordingAccessAuthorizationUrl_KeepsConfiguredScopesAndNeverDuplicatesARecordingScope()
    {
        var options = ConfiguredOptions();
        options.Scopes = ["openid", "email", GoogleScopes.CalendarEvents, GoogleScopes.MeetSpaceReadonly];

        var url = Create(new StubHttpHandler(), options).BuildRecordingAccessAuthorizationUrl("s", "c");

        Assert.Equal(
            $"openid email {GoogleScopes.CalendarEvents} {GoogleScopes.MeetSpaceReadonly} {GoogleScopes.DriveMeetReadonly}",
            HttpUtility.ParseQueryString(new Uri(url).Query)["scope"]);
    }

    [Fact]
    public void BuildRecordingAccessAuthorizationUrl_TheCalendarUrlIsUnchanged()
    {
        var query = HttpUtility.ParseQueryString(new Uri(Create(new StubHttpHandler()).BuildAuthorizationUrl("s", "c")).Query);

        Assert.Equal("false", query["include_granted_scopes"]);
        Assert.DoesNotContain("meetings.space.readonly", query["scope"]);
    }

    [Fact]
    public void BuildRecordingAccessAuthorizationUrl_WhenNotConfigured_Throws()
    {
        var service = Create(new StubHttpHandler(), new GoogleOAuthOptions());

        Assert.Throws<InvalidOperationException>(() => service.BuildRecordingAccessAuthorizationUrl("s", "c"));
    }

    // ---- hd -> HostedDomain ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("""{"sub":"1","email":"a@school.ac.th","email_verified":true,"hd":"school.ac.th"}""", "school.ac.th")]
    [InlineData("""{"sub":"1","email":"a@school.ac.th","email_verified":true,"hd":" School.AC.th "}""", "school.ac.th")]
    [InlineData("""{"sub":"1","email":"a@gmail.com","email_verified":true}""", null)]
    [InlineData("""{"sub":"1","email":"a@gmail.com","email_verified":true,"hd":""}""", null)]
    [InlineData("""{"sub":"1","email":"a@x.com","email_verified":true,"hd":"not a domain!"}""", null)]
    [InlineData("""{"sub":"1","email":"a@x.com","email_verified":true,"hd":"-bad.example"}""", null)]
    [InlineData("""{"sub":"1","email":"a@x.com","email_verified":true,"hd":123}""", null)]
    public async Task GetUserInfoAsync_ReadsTheHostedDomainFromHd(string body, string? expected)
    {
        var result = await Create(StubHttpHandler.Always(HttpStatusCode.OK, body)).GetUserInfoAsync("ya29.t", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.HostedDomain);
    }

    [Fact]
    public async Task GetUserInfoAsync_NoHdInUserInfo_FallsBackToTheIdTokenOfTheTokenResponseThatIssuedTheAccessToken()
    {
        var tokenBody = new JsonObject
        {
            ["access_token"] = "ya29.workspace",
            ["expires_in"] = 3599,
            ["refresh_token"] = "1//r",
            ["scope"] = "openid email",
            ["id_token"] = IdToken(new { sub = "1", hd = "School.ac.th" }),
        }.ToJsonString();
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, tokenBody)
            .Enqueue(HttpStatusCode.OK, """{"sub":"1","email":"a@school.ac.th","email_verified":true}""");
        var service = Create(handler);

        var tokens = await service.ExchangeCodeAsync("code", "verifier", CancellationToken.None);
        var user = await service.GetUserInfoAsync(tokens.Value.AccessToken, CancellationToken.None);

        Assert.Equal("school.ac.th", user.Value.HostedDomain);
    }

    [Fact]
    public async Task GetUserInfoAsync_TheIdTokenFallbackOnlyAppliesToTheTokenItCameWith()
    {
        var tokenBody = new JsonObject
        {
            ["access_token"] = "ya29.workspace",
            ["expires_in"] = 3599,
            ["id_token"] = IdToken(new { hd = "school.ac.th" }),
        }.ToJsonString();
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, tokenBody)
            .Enqueue(HttpStatusCode.OK, """{"sub":"2","email":"b@gmail.com","email_verified":true}""");
        var service = Create(handler);

        await service.RefreshAccessTokenAsync("1//r", CancellationToken.None);
        var other = await service.GetUserInfoAsync("ya29.somebody-else", CancellationToken.None);

        Assert.Null(other.Value.HostedDomain);
    }

    [Fact]
    public async Task GetUserInfoAsync_UserInfoHdWinsOverTheIdToken()
    {
        var tokenBody = new JsonObject
        {
            ["access_token"] = "ya29.t",
            ["expires_in"] = 3599,
            ["id_token"] = IdToken(new { hd = "old.example" }),
        }.ToJsonString();
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, tokenBody)
            .Enqueue(HttpStatusCode.OK, """{"sub":"1","email":"a@new.example","email_verified":true,"hd":"new.example"}""");
        var service = Create(handler);

        await service.RefreshAccessTokenAsync("1//r", CancellationToken.None);
        var user = await service.GetUserInfoAsync("ya29.t", CancellationToken.None);

        Assert.Equal("new.example", user.Value.HostedDomain);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("a.!!!.c")]
    [InlineData("")]
    [InlineData(null)]
    public void ReadHostedDomainFromIdToken_MalformedToken_IsNullNeverThrows(string? idToken)
    {
        Assert.Null(GoogleOAuthService.ReadHostedDomainFromIdToken(idToken));
    }

    [Fact]
    public void ReadHostedDomainFromIdToken_ReadsAUrlSafeBase64Payload()
    {
        Assert.Equal("school.ac.th", GoogleOAuthService.ReadHostedDomainFromIdToken(IdToken(new { hd = "school.ac.th", name = "ภาษาไทย ~~~??" })));
    }

    // ---- the fake OAuth service ---------------------------------------------------------------------------------

    [Fact]
    public void Fake_BuildRecordingAccessAuthorizationUrl_RedirectsBackWithTheRecordingCode()
    {
        var url = CreateFake().BuildRecordingAccessAuthorizationUrl("the state", "challenge");

        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.StartsWith("http://localhost:5190/api/live/instructor/google/callback?", url);
        Assert.Equal("dev-recording", query["code"]);
        Assert.Equal("the state", query["state"]);
    }

    [Fact]
    public async Task Fake_ExchangeOfThePlainCode_GrantsCalendarOnly_AndOfTheRecordingCode_GrantsBothRecordingScopes()
    {
        var fake = CreateFake();

        var plain = await fake.ExchangeCodeAsync("dev", "v", CancellationToken.None);
        var recording = await fake.ExchangeCodeAsync("dev-recording", "v", CancellationToken.None);

        Assert.False(GoogleScopes.HasRecordingScopes(plain.Value.GrantedScopes));
        Assert.True(GoogleScopes.HasCalendarScope(plain.Value.GrantedScopes));
        Assert.True(GoogleScopes.HasRecordingScopes(recording.Value.GrantedScopes));
        Assert.True(GoogleScopes.HasCalendarScope(recording.Value.GrantedScopes));
        Assert.StartsWith("dev-", recording.Value.RefreshToken);
    }

    [Fact]
    public async Task Fake_RefreshOfARecordingGrant_KeepsReportingTheRecordingScopes()
    {
        var fake = CreateFake();
        var recording = await fake.ExchangeCodeAsync("dev-recording", "v", CancellationToken.None);

        var refreshed = await fake.RefreshAccessTokenAsync(recording.Value.RefreshToken!, CancellationToken.None);
        var plainRefresh = await fake.RefreshAccessTokenAsync("dev-refresh-token", CancellationToken.None);

        Assert.True(GoogleScopes.HasRecordingScopes(refreshed.Value.GrantedScopes));
        Assert.StartsWith("dev-", refreshed.Value.AccessToken);
        Assert.False(GoogleScopes.HasRecordingScopes(plainRefresh.Value.GrantedScopes));
    }

    [Fact]
    public async Task Fake_UserInfo_DefaultAccountIsPersonal()
    {
        var user = await CreateFake().GetUserInfoAsync("dev-access-token", CancellationToken.None);

        Assert.Equal("dev-instructor@example.test", user.Value.Email);
        Assert.Null(user.Value.HostedDomain);
    }

    [Theory]
    [InlineData("teacher@workspace.example.test", "workspace.example.test")]
    [InlineData("Teacher@Workspace.Example.TEST", "workspace.example.test")]
    [InlineData("teacher@gmail.example.test", null)]
    [InlineData("teacher@notworkspace.example.test", null)]
    public async Task Fake_UserInfo_ReportsAHostedDomainOnlyForWorkspaceExampleTestAccounts(string email, string? expectedDomain)
    {
        var user = await CreateFake(email).GetUserInfoAsync("dev-access-token", CancellationToken.None);

        Assert.Equal(email, user.Value.Email);
        Assert.Equal(expectedDomain, user.Value.HostedDomain);
    }

    // ---- DevAccountEmail option --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("teacher@workspace.example.test")]
    public void Validate_DevAccountEmail_AcceptsEmptyOrASingleAddress(string email)
    {
        var options = ConfiguredOptions();
        options.DevAccountEmail = email;

        Assert.True(new GoogleOAuthOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("two@@example.test")]
    [InlineData("no-at-sign")]
    [InlineData("has space@example.test")]
    public void Validate_DevAccountEmail_RejectsAnythingThatIsNotOneAddress(string email)
    {
        var options = ConfiguredOptions();
        options.DevAccountEmail = email;

        var result = new GoogleOAuthOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(GoogleOAuthOptions.DevAccountEmail), StringComparison.Ordinal));
    }
}
