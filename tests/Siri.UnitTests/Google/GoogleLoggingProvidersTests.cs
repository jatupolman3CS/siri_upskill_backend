using System.Web;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Integrations.Google.Logging;

namespace Siri.UnitTests.Google;

/// <summary>The dev-only fakes behind <c>Live:Provider=Logging</c>: deterministic, clearly fake, never real credentials.</summary>
public sealed class GoogleLoggingProvidersTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private static LoggingGoogleOAuthService CreateOAuth(string redirectUri = "http://localhost:5190/api/live/instructor/google/callback") =>
        new(
            Options.Create(new GoogleOAuthOptions { RedirectUri = redirectUri }),
            new FixedClock(Now),
            new CapturingLogger<LoggingGoogleOAuthService>());

    private static LoggingCalendarProvider CreateCalendar(CapturingLogger<LoggingCalendarProvider>? logger = null) =>
        new(logger ?? new CapturingLogger<LoggingCalendarProvider>());

    [Fact]
    public void OAuth_IsConfiguredWithoutAnyClientId()
    {
        Assert.True(CreateOAuth().IsConfigured);
    }

    [Fact]
    public void OAuth_BuildAuthorizationUrl_RedirectsStraightBackToTheLocalCallback()
    {
        var url = CreateOAuth().BuildAuthorizationUrl("the state", "challenge");

        Assert.StartsWith("http://localhost:5190/api/live/instructor/google/callback?", url);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("dev", query["code"]);
        Assert.Equal("the state", query["state"]);
    }

    [Fact]
    public void OAuth_BuildAuthorizationUrl_AppendsToAnExistingQueryString()
    {
        var url = CreateOAuth("http://localhost:5190/cb?x=1").BuildAuthorizationUrl("s", "c");

        Assert.Equal("http://localhost:5190/cb?x=1&code=dev&state=s", url);
    }

    [Fact]
    public void OAuth_BuildAuthorizationUrl_WithoutRedirectUri_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateOAuth(redirectUri: string.Empty).BuildAuthorizationUrl("s", "c"));
    }

    [Fact]
    public async Task OAuth_ExchangeAndRefresh_ReturnClearlyFakeTokens()
    {
        var oauth = CreateOAuth();

        var exchange = await oauth.ExchangeCodeAsync("dev", "verifier", CancellationToken.None);
        var refresh = await oauth.RefreshAccessTokenAsync(exchange.Value.RefreshToken!, CancellationToken.None);

        Assert.StartsWith("dev-", exchange.Value.AccessToken);
        Assert.StartsWith("dev-", exchange.Value.RefreshToken);
        Assert.Equal(Now.AddHours(1), exchange.Value.ExpiresAtUtc);
        Assert.True(GoogleScopes.HasCalendarScope(exchange.Value.GrantedScopes));
        Assert.True(refresh.IsSuccess);
        Assert.Null(refresh.Value.RefreshToken);
    }

    [Fact]
    public async Task OAuth_Refresh_RejectsATokenTheFakeDidNotIssue_SoReconnectPathsStayTestable()
    {
        var result = await CreateOAuth().RefreshAccessTokenAsync("1//a-real-looking-token", CancellationToken.None);

        Assert.Equal(GoogleErrors.UnauthorizedCode, result.Error.Code);
        Assert.Equal("invalid_grant", result.Error.Reason);
    }

    [Fact]
    public async Task OAuth_UserInfo_IsTheFakeInstructor_AndRejectsForeignTokens()
    {
        var oauth = CreateOAuth();

        var ok = await oauth.GetUserInfoAsync("dev-access-token", CancellationToken.None);
        var rejected = await oauth.GetUserInfoAsync("ya29.real", CancellationToken.None);

        Assert.Equal("dev-instructor@example.test", ok.Value.Email);
        Assert.True(ok.Value.EmailVerified);
        Assert.Equal(GoogleErrors.UnauthorizedCode, rejected.Error.Code);
    }

    [Fact]
    public async Task OAuth_Revoke_AlwaysSucceeds()
    {
        Assert.True((await CreateOAuth().RevokeAsync("anything", CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Calendar_Create_ReturnsADeterministicUnresolvableMeetUrl()
    {
        var calendar = CreateCalendar();
        var request = new CalendarEventRequest("s", null, Now, Now.AddHours(1), "req-1", "sess-1");

        var first = await calendar.CreateEventWithMeetAsync("dev-access-token", request, CancellationToken.None);
        var second = await calendar.CreateEventWithMeetAsync("dev-access-token", request, CancellationToken.None);

        Assert.Equal(first.Value, second.Value);
        Assert.Equal("dev-req-1", first.Value.EventId);
        Assert.Equal("https://meet.invalid/dev/req-1", first.Value.MeetUrl);
        Assert.False(first.Value.ConferencePending);
        // .invalid is reserved by RFC 2606 - the fake URL can never reach a real host.
        Assert.EndsWith(".invalid", new Uri(first.Value.MeetUrl!).Host);
    }

    [Fact]
    public async Task Calendar_GetAndUpdate_KnownDevEvent_ReturnsItsUrl_UnknownIsNotFound()
    {
        var calendar = CreateCalendar();
        var request = new CalendarEventRequest("s", null, Now, Now.AddHours(1), "r", "p");

        var get = await calendar.GetEventAsync("t", "dev-abc", CancellationToken.None);
        var update = await calendar.UpdateEventAsync("t", "dev-abc", request, CancellationToken.None);
        var unknown = await calendar.GetEventAsync("t", "real-google-id", CancellationToken.None);
        var unknownUpdate = await calendar.UpdateEventAsync("t", "real-google-id", request, CancellationToken.None);

        Assert.Equal("https://meet.invalid/dev/abc", get.Value.MeetUrl);
        Assert.Equal(get.Value, update.Value);
        Assert.Equal(GoogleErrors.NotFoundCode, unknown.Error.Code);
        Assert.Equal(GoogleErrors.NotFoundCode, unknownUpdate.Error.Code);
    }

    [Fact]
    public async Task Calendar_FindDeleteAndAttendees_AreHarmless()
    {
        var calendar = CreateCalendar();

        var find = await calendar.FindEventByPrivateSessionIdAsync("t", "p", CancellationToken.None);
        var delete = await calendar.DeleteEventAsync("t", "dev-abc", CancellationToken.None);
        var attendees = await calendar.SetAttendeesAsync("t", "dev-abc", ["a@example.com"], CancellationToken.None);

        Assert.True(find.IsSuccess);
        Assert.Null(find.Value);
        Assert.True(delete.IsSuccess);
        Assert.True(attendees.IsSuccess);
    }

    [Fact]
    public async Task Calendar_NeverLogsAttendeeAddressesOrUrls()
    {
        var logger = new CapturingLogger<LoggingCalendarProvider>();
        var calendar = CreateCalendar(logger);

        await calendar.CreateEventWithMeetAsync("t", new CalendarEventRequest("s", null, Now, Now.AddHours(1), "req-9", "p"), CancellationToken.None);
        await calendar.SetAttendeesAsync("t", "dev-req-9", ["private.learner@example.com"], CancellationToken.None);

        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain("private.learner@example.com", logger.AllText);
        Assert.DoesNotContain("meet.invalid", logger.AllText);
    }
}
