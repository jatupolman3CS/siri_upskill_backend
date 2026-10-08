using System.Net;
using System.Web;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google;

namespace Siri.UnitTests.Google;

public sealed class GoogleOAuthServiceTests
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

    private static GoogleOAuthService Create(
        StubHttpHandler handler,
        GoogleOAuthOptions? options = null,
        CapturingLogger<GoogleOAuthService>? logger = null) =>
        new(
            new StubHttpClientFactory(handler),
            Options.Create(options ?? ConfiguredOptions()),
            new FixedClock(Now),
            logger ?? new CapturingLogger<GoogleOAuthService>());

    // ---- BuildAuthorizationUrl ---------------------------------------------------------------------------------

    [Fact]
    public void BuildAuthorizationUrl_UsesTheDocumentedEndpointAndParameters()
    {
        var service = Create(new StubHttpHandler());

        var url = service.BuildAuthorizationUrl("state-abc", "challenge-xyz");

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal(RedirectUri, query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid email https://www.googleapis.com/auth/calendar.events.owned", query["scope"]);
        Assert.Equal("state-abc", query["state"]);
        Assert.Equal("challenge-xyz", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal("false", query["include_granted_scopes"]);
        Assert.Equal(10, query.Count);
    }

    [Fact]
    public void BuildAuthorizationUrl_PercentEncodesValues_NoSecretInUrl()
    {
        var service = Create(new StubHttpHandler());

        var url = service.BuildAuthorizationUrl("a b&c=d", "ch");

        Assert.Contains("state=a%20b%26c%3Dd", url);
        Assert.Contains("scope=openid%20email%20https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fcalendar.events.owned", url);
        Assert.Contains($"redirect_uri={Uri.EscapeDataString(RedirectUri)}", url);
        Assert.DoesNotContain(ClientSecret, url);
    }

    [Fact]
    public void BuildAuthorizationUrl_HonoursConfiguredScopes()
    {
        var options = ConfiguredOptions();
        options.Scopes = ["openid", "email", GoogleScopes.CalendarEvents];

        var url = Create(new StubHttpHandler(), options).BuildAuthorizationUrl("s", "c");

        Assert.Equal(
            $"openid email {GoogleScopes.CalendarEvents}",
            HttpUtility.ParseQueryString(new Uri(url).Query)["scope"]);
    }

    [Fact]
    public void BuildAuthorizationUrl_WhenNotConfigured_Throws()
    {
        var service = Create(new StubHttpHandler(), new GoogleOAuthOptions());

        Assert.False(service.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => service.BuildAuthorizationUrl("s", "c"));
    }

    // ---- ExchangeCodeAsync -------------------------------------------------------------------------------------

    private const string TokenSuccessBody =
        """{"access_token":"ya29.access-token-value","expires_in":3920,"refresh_token":"1//refresh-token-value","scope":"openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/calendar.events.owned","token_type":"Bearer","id_token":"eyJ.id.token"}""";

    [Fact]
    public async Task ExchangeCodeAsync_PostsTheDocumentedFormToTheTokenEndpoint()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, TokenSuccessBody);

        var result = await Create(handler).ExchangeCodeAsync("auth-code-1", "verifier-1", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://oauth2.googleapis.com/token", request.Uri.AbsoluteUri);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.Null(request.Authorization);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["code"] = "auth-code-1",
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["redirect_uri"] = RedirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = "verifier-1",
            },
            request.Form);

        Assert.True(result.IsSuccess);
        Assert.Equal("ya29.access-token-value", result.Value.AccessToken);
        Assert.Equal("1//refresh-token-value", result.Value.RefreshToken);
        Assert.Equal(Now.AddSeconds(3920), result.Value.ExpiresAtUtc);
        Assert.Equal(
            "openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/calendar.events.owned",
            result.Value.GrantedScopes);
    }

    [Fact]
    public async Task ExchangeCodeAsync_Success_NeverPutsTheSecretInTheUrl()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, TokenSuccessBody);

        await Create(handler).ExchangeCodeAsync("auth-code-1", "verifier-1", CancellationToken.None);

        Assert.DoesNotContain("auth-code-1", handler.Requests[0].Uri.AbsoluteUri);
        Assert.DoesNotContain(ClientSecret, handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task ExchangeCodeAsync_WithoutRefreshTokenInResponse_ReturnsNullRefreshToken()
    {
        var handler = StubHttpHandler.Always(
            HttpStatusCode.OK,
            """{"access_token":"a","expires_in":3600,"scope":"openid","token_type":"Bearer"}""");

        var result = await Create(handler).ExchangeCodeAsync("c", "v", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.RefreshToken);
    }

    [Fact]
    public async Task ExchangeCodeAsync_MissingExpiresIn_FallsBackToOneHour()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, """{"access_token":"a","scope":"openid"}""");

        var result = await Create(handler).ExchangeCodeAsync("c", "v", CancellationToken.None);

        Assert.Equal(Now.AddHours(1), result.Value.ExpiresAtUtc);
    }

    [Fact]
    public async Task ExchangeCodeAsync_200WithoutAccessToken_IsTransient()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, """{"token_type":"Bearer"}""");

        var result = await Create(handler).ExchangeCodeAsync("c", "v", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Theory]
    [InlineData("", "v")]
    [InlineData("c", "")]
    public async Task ExchangeCodeAsync_MissingCodeOrVerifier_IsBadRequestWithoutCallingGoogle(string code, string verifier)
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).ExchangeCodeAsync(code, verifier, CancellationToken.None);

        Assert.Equal(GoogleErrors.BadRequestCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    // ---- token endpoint error mapping --------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Bad Request"}""", GoogleErrors.UnauthorizedCode, "invalid_grant")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_client","error_description":"The OAuth client was not found."}""", GoogleErrors.UnauthorizedCode, "invalid_client")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"unauthorized_client"}""", GoogleErrors.UnauthorizedCode, "unauthorized_client")]
    [InlineData(HttpStatusCode.Unauthorized, "", GoogleErrors.UnauthorizedCode, null)]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_request","error_description":"Missing required parameter: code"}""", GoogleErrors.BadRequestCode, "invalid_request")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"redirect_uri_mismatch"}""", GoogleErrors.BadRequestCode, "redirect_uri_mismatch")]
    [InlineData(HttpStatusCode.BadRequest, "<html>not json</html>", GoogleErrors.BadRequestCode, null)]
    [InlineData(HttpStatusCode.TooManyRequests, "", GoogleErrors.RateLimitedCode, null)]
    [InlineData(HttpStatusCode.InternalServerError, "", GoogleErrors.TransientCode, null)]
    [InlineData(HttpStatusCode.BadGateway, "", GoogleErrors.TransientCode, null)]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":"backend_error"}""", GoogleErrors.TransientCode, "backend_error")]
    [InlineData(HttpStatusCode.RequestTimeout, "", GoogleErrors.TransientCode, null)]
    public async Task ExchangeAndRefresh_ErrorResponses_MapToTheContractCodes(HttpStatusCode status, string body, string expectedCode, string? expectedReason)
    {
        var exchange = await Create(StubHttpHandler.Always(status, body)).ExchangeCodeAsync("c", "v", CancellationToken.None);
        var refresh = await Create(StubHttpHandler.Always(status, body)).RefreshAccessTokenAsync("rt", CancellationToken.None);

        foreach (var result in new[] { exchange, refresh })
        {
            Assert.True(result.IsFailure);
            Assert.Equal(expectedCode, result.Error.Code);
            Assert.Equal(expectedReason, result.Error.Reason);
        }
    }

    [Fact]
    public async Task ErrorMessages_NeverEchoGoogleFreeTextOrSecrets()
    {
        var handler = StubHttpHandler.Always(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Token has been expired or revoked for user someone@example.com refresh 1//zzz"}""");

        var result = await Create(handler).RefreshAccessTokenAsync("1//zzz", CancellationToken.None);

        Assert.DoesNotContain("someone@example.com", result.Error.Message);
        Assert.DoesNotContain("1//zzz", result.Error.Message);
        Assert.DoesNotContain(ClientSecret, result.Error.Message);
    }

    [Fact]
    public async Task Reason_IsSanitisedToAnAsciiToken()
    {
        var handler = StubHttpHandler.Always(
            HttpStatusCode.BadRequest,
            "{\"error\":\"bad value\\nwith <script>alert(1)</script> and a very long tail " + new string('x', 200) + "\"}");

        var result = await Create(handler).ExchangeCodeAsync("c", "v", CancellationToken.None);

        Assert.NotNull(result.Error.Reason);
        Assert.Matches("^[A-Za-z0-9_.-]{1,60}$", result.Error.Reason);
    }

    // ---- RefreshAccessTokenAsync -------------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAccessTokenAsync_PostsRefreshGrantAndReturnsNoNewRefreshToken()
    {
        var handler = StubHttpHandler.Always(
            HttpStatusCode.OK,
            """{"access_token":"ya29.fresh","expires_in":3599,"scope":"openid email https://www.googleapis.com/auth/calendar.events.owned","token_type":"Bearer"}""");

        var result = await Create(handler).RefreshAccessTokenAsync("1//the-refresh-token", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://oauth2.googleapis.com/token", request.Uri.AbsoluteUri);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["refresh_token"] = "1//the-refresh-token",
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["grant_type"] = "refresh_token",
            },
            request.Form);
        Assert.True(result.IsSuccess);
        Assert.Equal("ya29.fresh", result.Value.AccessToken);
        Assert.Null(result.Value.RefreshToken);
        Assert.Equal(Now.AddSeconds(3599), result.Value.ExpiresAtUtc);
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_EmptyRefreshToken_IsUnauthorizedWithoutCallingGoogle()
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).RefreshAccessTokenAsync(string.Empty, CancellationToken.None);

        Assert.Equal(GoogleErrors.UnauthorizedCode, result.Error.Code);
        Assert.Equal("invalid_grant", result.Error.Reason);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_RotatedRefreshTokenInResponse_IsReturned()
    {
        var handler = StubHttpHandler.Always(
            HttpStatusCode.OK,
            """{"access_token":"a","expires_in":3600,"refresh_token":"1//rotated","scope":"openid"}""");

        var result = await Create(handler).RefreshAccessTokenAsync("old", CancellationToken.None);

        Assert.Equal("1//rotated", result.Value.RefreshToken);
    }

    // ---- GetUserInfoAsync --------------------------------------------------------------------------------------

    [Fact]
    public async Task GetUserInfoAsync_SendsBearerToTheUserInfoEndpoint()
    {
        var handler = StubHttpHandler.Always(
            HttpStatusCode.OK,
            """{"sub":"110169484474386276334","email":"instructor@example.com","email_verified":true,"name":"X"}""");

        var result = await Create(handler).GetUserInfoAsync("ya29.token", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://openidconnect.googleapis.com/v1/userinfo", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer ya29.token", request.Authorization);
        Assert.True(result.IsSuccess);
        Assert.Equal(new GoogleUserInfo("110169484474386276334", "instructor@example.com", true), result.Value);
    }

    [Theory]
    [InlineData("""{"sub":"1","email":"a@example.com","email_verified":"true"}""", true)]
    [InlineData("""{"sub":"1","email":"a@example.com","email_verified":false}""", false)]
    [InlineData("""{"sub":"1","email":"a@example.com"}""", false)]
    public async Task GetUserInfoAsync_ReadsEmailVerifiedLeniently(string body, bool expected)
    {
        var result = await Create(StubHttpHandler.Always(HttpStatusCode.OK, body)).GetUserInfoAsync("t", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.EmailVerified);
    }

    [Theory]
    [InlineData("""{"email":"a@example.com"}""")]
    [InlineData("""{"sub":"1"}""")]
    [InlineData("""{"sub":"","email":"a@example.com"}""")]
    public async Task GetUserInfoAsync_MissingSubjectOrEmail_IsBadRequest(string body)
    {
        var result = await Create(StubHttpHandler.Always(HttpStatusCode.OK, body)).GetUserInfoAsync("t", CancellationToken.None);

        Assert.Equal(GoogleErrors.BadRequestCode, result.Error.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, GoogleErrors.UnauthorizedCode)]
    [InlineData(HttpStatusCode.Forbidden, GoogleErrors.UnauthorizedCode)]
    [InlineData(HttpStatusCode.TooManyRequests, GoogleErrors.RateLimitedCode)]
    [InlineData(HttpStatusCode.InternalServerError, GoogleErrors.TransientCode)]
    public async Task GetUserInfoAsync_ErrorStatuses_MapToTheContractCodes(HttpStatusCode status, string expectedCode)
    {
        var result = await Create(StubHttpHandler.Always(status, "")).GetUserInfoAsync("t", CancellationToken.None);

        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tok en")]
    [InlineData("tok\r\nInjected: header")]
    public async Task GetUserInfoAsync_UnusableToken_IsUnauthorizedWithoutCallingGoogle(string token)
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).GetUserInfoAsync(token, CancellationToken.None);

        Assert.Equal(GoogleErrors.UnauthorizedCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    // ---- RevokeAsync -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)] // already revoked / invalid - the goal (a dead token) is met
    public async Task RevokeAsync_Google200Or400_IsSuccess(HttpStatusCode status)
    {
        var handler = StubHttpHandler.Always(status, """{"error":"invalid_token"}""");

        var result = await Create(handler).RevokeAsync("1//token-to-revoke", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://oauth2.googleapis.com/revoke", request.Uri.AbsoluteUri);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.Equal(new Dictionary<string, string> { ["token"] = "1//token-to-revoke" }, request.Form);
        Assert.DoesNotContain("token-to-revoke", request.Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, GoogleErrors.TransientCode)]
    [InlineData(HttpStatusCode.TooManyRequests, GoogleErrors.RateLimitedCode)]
    public async Task RevokeAsync_OtherStatuses_AreInformationalFailures(HttpStatusCode status, string expectedCode)
    {
        var result = await Create(StubHttpHandler.Always(status, "")).RevokeAsync("t", CancellationToken.None);

        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public async Task RevokeAsync_EmptyToken_IsSuccessWithoutCallingGoogle()
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).RevokeAsync(string.Empty, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(handler.Requests);
    }

    // ---- not configured / transport failures / logging --------------------------------------------------------

    [Fact]
    public async Task EveryCall_WhenNotConfigured_AnswersNotConfiguredWithoutCallingGoogle()
    {
        var handler = new StubHttpHandler();
        var service = Create(handler, new GoogleOAuthOptions());

        var results = new Siri.SharedKernel.Result[]
        {
            await service.ExchangeCodeAsync("c", "v", CancellationToken.None),
            await service.RefreshAccessTokenAsync("r", CancellationToken.None),
            await service.GetUserInfoAsync("t", CancellationToken.None),
            await service.RevokeAsync("t", CancellationToken.None),
        };

        Assert.All(results, r =>
        {
            Assert.True(r.IsFailure);
            Assert.Equal(GoogleErrors.NotConfiguredCode, r.Error.Code);
            // ".not_configured" suffix => the shared mapper answers 503 should it ever surface unmapped.
            Assert.EndsWith(Siri.SharedKernel.DomainErrorHttpResults.DotNotConfiguredCodeSuffix, r.Error.Code);
        });
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NetworkFailure_IsTransientNotAnException()
    {
        var handler = new StubHttpHandler().EnqueueThrow(new HttpRequestException("name resolution failed"));

        var result = await Create(handler).RefreshAccessTokenAsync("r", CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Fact]
    public async Task HttpClientTimeout_IsTransient()
    {
        var handler = new StubHttpHandler().EnqueueThrow(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var result = await Create(handler).ExchangeCodeAsync("c", "v", CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHttpHandler().EnqueueThrow(new TaskCanceledException("cancelled"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).RefreshAccessTokenAsync("r", cts.Token));
    }

    [Fact]
    public async Task NonJsonSuccessBody_IsTransientNotAnException()
    {
        // e.g. an HTML interstitial from a proxy: ReadFromJsonAsync would throw NotSupportedException.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, "<html>hello</html>", "text/html");

        var result = await Create(handler).ExchangeCodeAsync("c", "v", CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Fact]
    public async Task MalformedSuccessBody_IsTransient()
    {
        var result = await Create(StubHttpHandler.Always(HttpStatusCode.OK, "{not json")).ExchangeCodeAsync("c", "v", CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Fact]
    public async Task Secrets_NeverReachTheLogs_OnSuccessOrFailure()
    {
        var logger = new CapturingLogger<GoogleOAuthService>();
        const string code = "4/0AVeryDistinctiveAuthCode";
        const string verifier = "verifierDistinctive1234567890";
        const string refreshToken = "1//DistinctiveRefreshToken";

        foreach (var status in new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.InternalServerError })
        {
            var body = status == HttpStatusCode.OK
                ? TokenSuccessBody
                : """{"error":"invalid_grant","error_description":"leaks 4/0AVeryDistinctiveAuthCode and 1//DistinctiveRefreshToken"}""";
            var service = Create(StubHttpHandler.Always(status, body), logger: logger);

            await service.ExchangeCodeAsync(code, verifier, CancellationToken.None);
            await service.RefreshAccessTokenAsync(refreshToken, CancellationToken.None);
            await service.GetUserInfoAsync("ya29.DistinctiveAccessToken", CancellationToken.None);
            await service.RevokeAsync(refreshToken, CancellationToken.None);
        }

        var thrown = Create(new StubHttpHandler().EnqueueThrow(new HttpRequestException("boom")), logger: logger);
        await thrown.RefreshAccessTokenAsync(refreshToken, CancellationToken.None);

        var logs = logger.AllText;
        Assert.NotEmpty(logger.Lines);
        foreach (var secret in new[] { code, verifier, refreshToken, "ya29.DistinctiveAccessToken", "ya29.access-token-value", ClientSecret, "someone@example.com" })
        {
            Assert.DoesNotContain(secret, logs);
        }
    }
}
