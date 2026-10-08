using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// The Google connect flow end to end (P11-03) with <c>Live:Provider=Logging</c>, which swaps Google for the development-only fakes that issue
/// clearly fake <c>dev-*</c> tokens — so the REAL controller, state store (a real Redis), encryption, database, and sync job all run, with only the
/// outside world faked. Also proves the dev provider produces a usable room through the real job.
/// <para>
/// Requires Docker like every test in this collection.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveGoogleOAuthIntegrationTests : IAsyncLifetime
{
    private const string PublicBaseUrl = "https://app.example.test";
    private const string DefaultLanding = PublicBaseUrl + "/instructor/live-settings";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;
    private HttpClient _noRedirect = null!;

    public LiveGoogleOAuthIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers, new Dictionary<string, string?>
        {
            ["Live:Provider"] = "Logging",
            ["Live:PublicBaseUrl"] = PublicBaseUrl,
            ["Integrations:Google:RedirectUri"] = "http://localhost/api/live/instructor/google/callback",
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _client = _factory.CreateClient();
        _noRedirect = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _noRedirect.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<string> BeginConnectAsync(string token, string? returnPath = null)
    {
        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Post, "/api/live/instructor/google/connect", token);
        request.Content = JsonContent.Create(new { returnPath });

        var (status, body) = await LiveIntegrationSupport.SendAsync(_client, request);
        Assert.Equal(HttpStatusCode.OK, status);

        var url = new Uri(body.RootElement.GetProperty("authorizationUrl").GetString()!);
        var state = HttpUtility.ParseQueryString(url.Query)["state"];
        Assert.False(string.IsNullOrEmpty(state));
        return state!;
    }

    private async Task<HttpResponseMessage> CallbackAsync(string? state, string code = "dev", string? error = null)
    {
        var query = $"?code={Uri.EscapeDataString(code)}" + (state is null ? string.Empty : $"&state={Uri.EscapeDataString(state)}") + (error is null ? string.Empty : $"&error={error}");
        return await _noRedirect.GetAsync("/api/live/instructor/google/callback" + query); // anonymous: no bearer token on purpose
    }

    /// <summary>
    /// Runs the real <c>live-meeting-sync</c> job until this session's meeting is no longer due. The database is shared with every other test
    /// in the collection (and a run handles at most 50 meetings, oldest first), so one run is not guaranteed to reach this session.
    /// </summary>
    private async Task RunSyncJobUntilProcessedAsync(Guid sessionId)
    {
        for (var run = 0; run < 40; run++)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LiveMeetingSyncJob>().RunAsync(CancellationToken.None);

            var meeting = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SessionMeetings().AsNoTracking()
                .SingleAsync(m => m.SESSION_ID == sessionId);
            if (meeting.SYNC_STATUS is not (MeetingSyncStatus.Pending or MeetingSyncStatus.PendingDelete))
            {
                return;
            }
        }

        Assert.Fail("The sync job never processed the session's meeting.");
    }

    // ---- Status / connect ------------------------------------------------------------------------------

    [Fact]
    public async Task Status_WithTheFakeProvider_ReportsConfigured_AndNotConnectedYet()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);

        var (status, body) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Get, "/api/live/instructor/google/status", instructor.Token));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.RootElement.GetProperty("configured").GetBoolean());
        Assert.False(body.RootElement.GetProperty("connected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("googleEmail").ValueKind);
    }

    [Fact]
    public async Task Connect_ReturnsAnAuthorizationUrlWithAFreshStateEachTime()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);

        var first = await BeginConnectAsync(instructor.Token);
        var second = await BeginConnectAsync(instructor.Token);

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 43); // 32 random bytes, base64url
    }

    // ---- Callback ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Callback_ValidState_Redirects302ToTheSettingsPage_AndStoresTheRefreshTokenEncrypted()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var state = await BeginConnectAsync(instructor.Token);

        using var response = await CallbackAsync(state);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{DefaultLanding}?google=connected", response.Headers.Location!.ToString());
        Assert.DoesNotContain("dev-", response.Headers.Location.ToString()); // no token/code in the URL

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.InstructorGoogleAccounts().AsNoTracking().SingleAsync(a => a.INSTRUCTOR_USER_ID == instructor.UserId);
        Assert.True(account.IsActive);
        Assert.Equal("dev-instructor@example.test", account.GOOGLE_EMAIL);
        Assert.NotEqual("dev-refresh-token", account.REFRESH_TOKEN_ENCRYPTED);
        Assert.DoesNotContain("dev-refresh-token", account.REFRESH_TOKEN_ENCRYPTED);
        Assert.Equal("dev-refresh-token", scope.ServiceProvider.GetRequiredService<ISensitiveDataProtector>().Decrypt(account.REFRESH_TOKEN_ENCRYPTED!));
    }

    [Fact]
    public async Task Callback_HonoursASafeReturnPath()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var state = await BeginConnectAsync(instructor.Token, "/instructor/sessions");

        using var response = await CallbackAsync(state);

        Assert.Equal($"{PublicBaseUrl}/instructor/sessions?google=connected", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("https://evil.example.test/steal")]
    [InlineData("//evil.example.test")]
    [InlineData("/admin/users")]
    [InlineData("/instructor/../admin")]
    public async Task Callback_UnsafeReturnPath_FallsBackToTheDefaultLandingPage_NeverOffSite(string returnPath)
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var state = await BeginConnectAsync(instructor.Token, returnPath);

        using var response = await CallbackAsync(state);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{DefaultLanding}?google=connected", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_ReplayedState_IsRefused_SoAStateWorksExactlyOnce()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var state = await BeginConnectAsync(instructor.Token);

        using var first = await CallbackAsync(state);
        using var second = await CallbackAsync(state);

        Assert.Equal($"{DefaultLanding}?google=connected", first.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal($"{DefaultLanding}?google=error&reason=state_invalid", second.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("never-issued-state")]
    [InlineData("")]
    public async Task Callback_UnknownOrMissingState_Is302StateInvalid_NotA4xx(string? state)
    {
        using var response = await CallbackAsync(string.IsNullOrEmpty(state) ? null : state);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{DefaultLanding}?google=error&reason=state_invalid", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_UserDeniedConsent_Is302AccessDenied_AndStoresNothing()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var state = await BeginConnectAsync(instructor.Token);

        using var response = await CallbackAsync(state, error: "access_denied");

        Assert.Equal($"{DefaultLanding}?google=error&reason=access_denied", response.Headers.Location!.ToString());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.InstructorGoogleAccounts().AnyAsync(a => a.INSTRUCTOR_USER_ID == instructor.UserId));
    }

    [Fact]
    public async Task Callback_ConnectsTheInstructorWhoStartedTheFlow_NotWhoeverCallsBack()
    {
        var alice = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var bob = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var aliceState = await BeginConnectAsync(alice.Token);

        // The callback request carries no identity at all; the state alone says whose connection this is.
        using var response = await CallbackAsync(aliceState);

        Assert.Equal($"{DefaultLanding}?google=connected", response.Headers.Location!.ToString());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.InstructorGoogleAccounts().AnyAsync(a => a.INSTRUCTOR_USER_ID == alice.UserId));
        Assert.False(await db.InstructorGoogleAccounts().AnyAsync(a => a.INSTRUCTOR_USER_ID == bob.UserId));
    }

    // ---- Status after connect / disconnect -----------------------------------------------------------------

    [Fact]
    public async Task Status_AfterConnecting_ShowsTheAccount_AndDisconnectClearsTheTokenIdempotently()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        using (await CallbackAsync(await BeginConnectAsync(instructor.Token)))
        {
        }

        var (_, connected) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Get, "/api/live/instructor/google/status", instructor.Token));
        Assert.True(connected.RootElement.GetProperty("connected").GetBoolean());
        Assert.False(connected.RootElement.GetProperty("needsReconnect").GetBoolean());
        Assert.Equal("dev-instructor@example.test", connected.RootElement.GetProperty("googleEmail").GetString());
        LiveIntegrationSupport.AssertNoRoomUrl(connected, "dev-refresh-token", "dev-access-token");

        for (var i = 0; i < 2; i++)
        {
            using var disconnect = LiveIntegrationSupport.Authorized(HttpMethod.Delete, "/api/live/instructor/google", instructor.Token);
            using var response = await _client.SendAsync(disconnect);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var (_, afterwards) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Get, "/api/live/instructor/google/status", instructor.Token));
        Assert.False(afterwards.RootElement.GetProperty("connected").GetBoolean());
        Assert.False(afterwards.RootElement.GetProperty("needsReconnect").GetBoolean());
        Assert.Equal("user_disconnected", afterwards.RootElement.GetProperty("revokedReason").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.InstructorGoogleAccounts().AsNoTracking().SingleAsync(a => a.INSTRUCTOR_USER_ID == instructor.UserId);
        Assert.Null(account.REFRESH_TOKEN_ENCRYPTED);
        Assert.False(account.IsActive);
    }

    [Fact]
    public async Task Reconnect_AfterDisconnect_ReusesTheRow()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        using (await CallbackAsync(await BeginConnectAsync(instructor.Token)))
        {
        }

        using (var disconnect = LiveIntegrationSupport.Authorized(HttpMethod.Delete, "/api/live/instructor/google", instructor.Token))
        using (await _client.SendAsync(disconnect))
        {
        }

        using (await CallbackAsync(await BeginConnectAsync(instructor.Token)))
        {
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accounts = await db.InstructorGoogleAccounts().AsNoTracking().Where(a => a.INSTRUCTOR_USER_ID == instructor.UserId).ToListAsync();
        Assert.Single(accounts);
        Assert.True(accounts[0].IsActive);
    }

    // ---- The dev provider through the real job ---------------------------------------------------------------

    [Fact]
    public async Task SyncJob_WithTheFakeProvider_BuildsAUsableRoom_SoThePublishGatePasses()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        await RunSyncJobUntilProcessedAsync(sessionId);

        var (listStatus, list) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Get, $"/api/live/instructor/courses/{courseId}/meetings", instructor.Token));
        Assert.Equal(HttpStatusCode.OK, listStatus);
        var item = list.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sessionId").GetGuid() == sessionId);
        Assert.Equal("Logging", item.GetProperty("provider").GetString());
        Assert.Equal("Synced", item.GetProperty("syncStatus").GetString());
        Assert.True(item.GetProperty("isUsable").GetBoolean());
        LiveIntegrationSupport.AssertNoRoomUrl(list, "meet.invalid");

        var (submitStatus, _) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/submit-for-review", instructor.Token));
        Assert.Equal(HttpStatusCode.OK, submitStatus);

        // The fake room's URL is stored only as ciphertext.
        var stored = await LiveIntegrationSupport.ReadRawColumnAsync(_factory, "MEET_URL_ENCRYPTED", sessionId);
        Assert.DoesNotContain("meet.invalid", stored);
    }

    [Fact]
    public async Task SyncJob_ASessionCancelledBeforeAnyRoomExisted_EndsDeleted_AndTheGateIgnoresIt()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);
        using (var cancel = LiveIntegrationSupport.Authorized(HttpMethod.Delete, $"/api/catalog/instructor/courses/{courseId}/live-sessions/{sessionId}", instructor.Token))
        using (var cancelResponse = await _client.SendAsync(cancel))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);
        }

        await RunSyncJobUntilProcessedAsync(sessionId);

        await using var verify = _factory.Services.CreateAsyncScope();
        var meeting = await verify.ServiceProvider.GetRequiredService<AppDbContext>().SessionMeetings().AsNoTracking().SingleAsync(m => m.SESSION_ID == sessionId);
        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
    }
}
