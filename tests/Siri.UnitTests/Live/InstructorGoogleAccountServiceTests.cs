using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>The OAuth connection lifecycle: state handling (single-use, fail-closed), scope/refresh-token checks, encryption at rest, revocation
/// policy (invalid_grant revokes; invalid_client does NOT), and that no secret ever reaches a log.</summary>
public class InstructorGoogleAccountServiceTests
{
    private static readonly Guid InstructorId = Guid.NewGuid();
    private const string PostConnectPath = "/instructor/live-settings";

    private readonly InMemoryAccountRepository _accounts = new();
    private readonly InMemorySessionMeetingRepository _meetings = new();
    private readonly FakeGoogleOAuth _oauth = new();
    private readonly FakeStateStore _states = new();
    private readonly FakeSchedule _schedule = new();
    private readonly RecordingAlertSender _alerts = new();
    private readonly FakeClock _clock = new(LiveTestData.Now);
    private readonly ISensitiveDataProtector _protector = LiveTestData.Protector();
    private readonly ListLogger<InstructorGoogleAccountService> _logger = new();
    private LiveProviderMode _mode = LiveProviderMode.GoogleMeet;

    private InstructorGoogleAccountService Service() => new(
        _accounts,
        _meetings,
        _oauth,
        _states,
        _protector,
        _schedule,
        _alerts,
        _clock,
        LiveTestData.OptionsOf(o => o.Provider = _mode),
        Microsoft.Extensions.Options.Options.Create(new GoogleOAuthOptions { PostConnectRedirectPath = PostConnectPath }),
        _logger);

    /// <summary>Runs connect then returns the state a Google redirect would carry back.</summary>
    private async Task<string> BeginAsync(string? returnPath = null)
    {
        var result = await Service().BeginConnectAsync(InstructorId, returnPath, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return _oauth.LastState!;
    }

    private INSTRUCTOR_GOOGLE_ACCOUNT ActiveAccount(string refreshToken = "refresh-token-1")
    {
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(InstructorId, "sub-1", "teacher@gmail.test", _protector.Encrypt(refreshToken), GoogleScopes.CalendarEventsOwned, _clock);
        _accounts.Accounts.Add(account);
        return account;
    }

    // ---- Status -----------------------------------------------------------------------------------

    [Fact]
    public async Task GetStatus_NeverConnected_IsAllFalse()
    {
        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.True(status.Configured);
        Assert.False(status.Connected);
        Assert.False(status.NeedsReconnect);
        Assert.Null(status.GoogleEmail);
        Assert.Equal(0, status.AffectedSessionCount);
    }

    [Fact]
    public async Task GetStatus_FeatureOff_ReportsNotConfigured()
    {
        _oauth.Configured = false;

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.False(status.Configured);
    }

    [Fact]
    public async Task GetStatus_ManualOnlyMode_ReportsNotConfigured_EvenWithAClient()
    {
        _mode = LiveProviderMode.ManualOnly;

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.False(status.Configured);
    }

    [Fact]
    public async Task GetStatus_ActiveAccount_ShowsTheConnection()
    {
        ActiveAccount();

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.True(status.Connected);
        Assert.False(status.NeedsReconnect);
        Assert.Equal("teacher@gmail.test", status.GoogleEmail);
        Assert.Equal(LiveTestData.Now, status.ConnectedAtUtc);
        Assert.Null(status.RevokedReason);
    }

    [Fact]
    public async Task GetStatus_TokenRevoked_NeedsReconnect_AndStillShowsWhichAccount()
    {
        ActiveAccount().MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, _clock);

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.False(status.Connected);
        Assert.True(status.NeedsReconnect);
        Assert.Equal("teacher@gmail.test", status.GoogleEmail);
        Assert.Equal("invalid_grant", status.RevokedReason);
    }

    [Fact]
    public async Task GetStatus_DeliberateDisconnect_IsForgotten_NotAReconnectPrompt()
    {
        ActiveAccount().MarkRevoked(GoogleAccountRevokedReason.UserDisconnected, _clock);

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.False(status.Connected);
        Assert.False(status.NeedsReconnect);
        Assert.Null(status.GoogleEmail);
        Assert.Null(status.ConnectedAtUtc);
        Assert.Equal("user_disconnected", status.RevokedReason);
    }

    [Fact]
    public async Task GetStatus_CountsUpcomingSessionsStuckWithoutARoom()
    {
        ActiveAccount().MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, _clock);

        LiveSessionContext Session() => LiveTestData.Context(instructorUserId: InstructorId);
        var needsReconnect = Session();
        var awaitingLink = Session();
        var pending = Session();
        var needsReconnectButHasUrl = Session();
        var someoneElses = LiveTestData.Context(instructorUserId: Guid.NewGuid());
        _schedule.Contexts.AddRange([needsReconnect, awaitingLink, pending, needsReconnectButHasUrl, someoneElses]);

        var m1 = SESSION_MEETING.Stage(needsReconnect.SessionId);
        m1.RecordNeedsReconnect("invalid_grant");
        var m2 = SESSION_MEETING.Stage(awaitingLink.SessionId);
        m2.ResolveAsAwaitingLink();
        var m3 = SESSION_MEETING.Stage(pending.SessionId);
        var m4 = SESSION_MEETING.Stage(needsReconnectButHasUrl.SessionId);
        m4.SetManualLink(_protector.Encrypt("https://zoom.us/j/1"));
        m4.RecordNeedsReconnect("invalid_grant");
        var m5 = SESSION_MEETING.Stage(someoneElses.SessionId);
        m5.ResolveAsAwaitingLink();
        _meetings.Meetings.AddRange([m1, m2, m3, m4, m5]);

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Equal(2, status.AffectedSessionCount);
    }

    // ---- BeginConnect -----------------------------------------------------------------------------

    [Fact]
    public async Task BeginConnect_FeatureOff_Is503WithTheNotConfiguredReason()
    {
        _oauth.Configured = false;

        var result = await Service().BeginConnectAsync(InstructorId, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unavailable", result.Error.Code);
        Assert.Equal("live.google_not_configured", result.Error.Reason);
        Assert.Empty(_states.States);
    }

    [Fact]
    public async Task BeginConnect_ManualOnlyMode_Is503NotConfigured()
    {
        _mode = LiveProviderMode.ManualOnly;

        var result = await Service().BeginConnectAsync(InstructorId, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("live.google_not_configured", result.Error.Reason);
    }

    [Fact]
    public async Task BeginConnect_StoresASingleUseStateForTenMinutes_BoundToTheCallingInstructor()
    {
        var result = await Service().BeginConnectAsync(InstructorId, "/instructor/sessions/abc", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("https://accounts.example.test/auth", result.Value.AuthorizationUrl);

        var (state, payload) = Assert.Single(_states.States);
        Assert.Equal(_oauth.LastState, state);
        Assert.Equal(InstructorId, payload.UserId);
        Assert.Equal("/instructor/sessions/abc", payload.ReturnPath);
        Assert.Equal(GoogleOAuthPkce.CodeVerifierLength, payload.CodeVerifier.Length);
        Assert.Equal(TimeSpan.FromMinutes(10), _states.LastTimeToLive);
        Assert.True(state.Length >= 43);
        // The challenge sent to Google is derived from the stored verifier (PKCE S256).
        Assert.Contains(Uri.EscapeDataString(GoogleOAuthPkce.ComputeCodeChallenge(payload.CodeVerifier)), result.Value.AuthorizationUrl);
        Assert.DoesNotContain(payload.CodeVerifier, result.Value.AuthorizationUrl);
    }

    [Theory]
    [InlineData("https://evil.example.test/steal")]
    [InlineData("//evil.example.test")]
    [InlineData("/instructor/../admin")]
    [InlineData("/instructor//x")]
    [InlineData("/admin/users")]
    [InlineData("/instructor/x?next=https://evil.example.test")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    public async Task BeginConnect_UnsafeOrMissingReturnPath_FallsBackToTheConfiguredLandingPage(string? returnPath)
    {
        await Service().BeginConnectAsync(InstructorId, returnPath, CancellationToken.None);

        Assert.Equal(PostConnectPath, _states.States.Values.Single().ReturnPath);
    }

    [Fact]
    public async Task BeginConnect_StateStoreDown_FailsClosedWith503()
    {
        _states.Available = false;

        var result = await Service().BeginConnectAsync(InstructorId, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unavailable", result.Error.Code);
        Assert.Null(_oauth.LastState); // no authorization URL was ever built
    }

    // ---- CompleteConnect: state handling -----------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CompleteConnect_MissingState_RedirectsToTheLandingPageWithStateInvalid(string? state)
    {
        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        AssertError(outcome, "state_invalid", PostConnectPath);
        Assert.Empty(_accounts.Accounts);
    }

    [Fact]
    public async Task CompleteConnect_UnknownState_IsStateInvalid()
    {
        var outcome = await Service().CompleteConnectAsync("code", "never-issued", null, CancellationToken.None);

        AssertError(outcome, "state_invalid", PostConnectPath);
        Assert.Empty(_accounts.Accounts);
    }

    [Fact]
    public async Task CompleteConnect_OverlongState_IsStateInvalid_WithoutTouchingTheStore()
    {
        var outcome = await Service().CompleteConnectAsync("code", new string('s', 300), null, CancellationToken.None);

        AssertError(outcome, "state_invalid", PostConnectPath);
    }

    [Fact]
    public async Task CompleteConnect_ReplayedState_IsRefused_TheSecondTime()
    {
        var state = await BeginAsync();

        var first = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);
        var second = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Null(first.ErrorReason);
        AssertError(second, "state_invalid", PostConnectPath);
        Assert.Single(_accounts.Accounts);
    }

    [Fact]
    public async Task CompleteConnect_StateStoreDown_IsStateInvalid_NeverAConnection()
    {
        var state = await BeginAsync();
        _states.Available = false;

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        AssertError(outcome, "state_invalid", PostConnectPath);
        Assert.Empty(_accounts.Accounts);
    }

    [Fact]
    public async Task CompleteConnect_UserDeniedConsent_RedirectsToTheReturnPathWithAccessDenied()
    {
        var state = await BeginAsync("/instructor/sessions/abc");

        var outcome = await Service().CompleteConnectAsync(null, state, "access_denied", CancellationToken.None);

        AssertError(outcome, "access_denied", "/instructor/sessions/abc");
        Assert.Empty(_accounts.Accounts);
    }

    [Fact]
    public async Task CompleteConnect_OtherGoogleError_IsExchangeFailed()
    {
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync(null, state, "server_error", CancellationToken.None);

        AssertError(outcome, "exchange_failed", PostConnectPath);
    }

    [Fact]
    public async Task CompleteConnect_MissingCode_IsExchangeFailed()
    {
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync(null, state, null, CancellationToken.None);

        AssertError(outcome, "exchange_failed", PostConnectPath);
    }

    // ---- CompleteConnect: exchange --------------------------------------------------------------------

    [Fact]
    public async Task CompleteConnect_Success_StoresTheRefreshTokenEncrypted_AndRedirectsToTheReturnPath()
    {
        var state = await BeginAsync("/instructor/sessions/abc");

        var outcome = await Service().CompleteConnectAsync("auth-code", state, null, CancellationToken.None);

        Assert.Null(outcome.ErrorReason);
        Assert.Equal("https://app.example.test/instructor/sessions/abc?google=connected", outcome.RedirectUrl);

        var account = Assert.Single(_accounts.Accounts);
        Assert.Equal(InstructorId, account.INSTRUCTOR_USER_ID);
        Assert.Equal("teacher@gmail.test", account.GOOGLE_EMAIL);
        Assert.True(account.IsActive);
        Assert.NotEqual("refresh-token-1", account.REFRESH_TOKEN_ENCRYPTED);
        Assert.DoesNotContain("refresh-token-1", account.REFRESH_TOKEN_ENCRYPTED);
        Assert.Equal("refresh-token-1", _protector.Decrypt(account.REFRESH_TOKEN_ENCRYPTED!));
        Assert.Equal(1, _accounts.SaveCount);
    }

    [Fact]
    public async Task CompleteConnect_SendsTheStoredPkceVerifierToGoogle()
    {
        var state = await BeginAsync();
        var storedVerifier = _states.States[state].CodeVerifier;

        await Service().CompleteConnectAsync("auth-code", state, null, CancellationToken.None);

        Assert.Equal(storedVerifier, _oauth.LastCodeVerifier);
    }

    [Fact]
    public async Task CompleteConnect_RedirectUrl_NeverContainsTheCodeATokenOrAnEmail()
    {
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync("super-secret-auth-code", state, null, CancellationToken.None);

        Assert.DoesNotContain("super-secret-auth-code", outcome.RedirectUrl);
        Assert.DoesNotContain("refresh-token-1", outcome.RedirectUrl);
        Assert.DoesNotContain("access-token", outcome.RedirectUrl);
        Assert.DoesNotContain("gmail.test", outcome.RedirectUrl);
        Assert.DoesNotContain(state, outcome.RedirectUrl);
    }

    [Fact]
    public async Task CompleteConnect_CalendarScopeNotGranted_StoresNothing_AndRevokesWhatGoogleIssued()
    {
        _oauth.ExchangeResult = Result.Success(new GoogleTokenSet("access-x", LiveTestData.Now.AddHours(1), "refresh-x", "openid email"));
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        AssertError(outcome, "scope_missing", PostConnectPath);
        Assert.Empty(_accounts.Accounts);
        Assert.Equal(["refresh-x"], _oauth.RevokedTokens);
    }

    [Theory]
    [InlineData("https://www.googleapis.com/auth/calendar.readonly")]
    [InlineData("https://www.googleapis.com/auth/calendar.events.readonly")]
    [InlineData("https://www.googleapis.com/auth/calendar.events.owned.evil")]
    public async Task CompleteConnect_ReadOnlyOrLookAlikeScope_IsNotEnough(string grantedCalendarScope)
    {
        _oauth.ExchangeResult = Result.Success(new GoogleTokenSet("a", LiveTestData.Now.AddHours(1), "r", $"openid email {grantedCalendarScope}"));
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        AssertError(outcome, "scope_missing", PostConnectPath);
        Assert.Empty(_accounts.Accounts);
    }

    [Fact]
    public async Task CompleteConnect_NoRefreshTokenReturned_StoresNothing()
    {
        _oauth.ExchangeResult = Result.Success(new GoogleTokenSet("access-only", LiveTestData.Now.AddHours(1), null, GoogleScopes.Calendar));
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        AssertError(outcome, "no_refresh_token", PostConnectPath);
        Assert.Empty(_accounts.Accounts);
        Assert.Equal(["access-only"], _oauth.RevokedTokens);
    }

    [Fact]
    public async Task CompleteConnect_ExchangeRejectedByGoogle_IsExchangeFailed()
    {
        _oauth.ExchangeResult = Result.Failure<GoogleTokenSet>(GoogleErrors.BadRequest("nope", "invalid_grant"));
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        AssertError(outcome, "exchange_failed", PostConnectPath);
        Assert.Empty(_accounts.Accounts);
    }

    [Fact]
    public async Task CompleteConnect_ClientCredentialsRejected_IsLoggedAsAnOperatorErrorAndExchangeFailed()
    {
        _oauth.ExchangeResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Unauthorized("rejected", "invalid_client"));
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        AssertError(outcome, "exchange_failed", PostConnectPath);
        Assert.Contains("invalid_client", _logger.All);
    }

    [Fact]
    public async Task CompleteConnect_UserInfoFails_StoresNothing_AndRevokesTheRefreshToken()
    {
        _oauth.UserInfoResult = Result.Failure<GoogleUserInfo>(GoogleErrors.Transient("down"));
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        AssertError(outcome, "exchange_failed", PostConnectPath);
        Assert.Empty(_accounts.Accounts);
        Assert.Contains("refresh-token-1", _oauth.RevokedTokens);
    }

    [Fact]
    public async Task CompleteConnect_AnUnexpectedException_StillEndsInARedirect_AndLogsOnlyTheType()
    {
        var state = await BeginAsync();
        var throwingProtector = new ThrowingProtector();
        var service = new InstructorGoogleAccountService(
            _accounts, _meetings, _oauth, _states, throwingProtector, _schedule, _alerts, _clock, LiveTestData.OptionsOf(),
            Microsoft.Extensions.Options.Options.Create(new GoogleOAuthOptions { PostConnectRedirectPath = PostConnectPath }), _logger);

        var outcome = await service.CompleteConnectAsync("code", state, null, CancellationToken.None);

        AssertError(outcome, "exchange_failed", PostConnectPath);
        Assert.DoesNotContain("refresh-token-1", _logger.All);
    }

    [Fact]
    public async Task CompleteConnect_ReconnectAfterRevocation_ReusesTheRowAndClearsTheRevocation()
    {
        var existing = ActiveAccount("old-refresh");
        existing.MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, _clock);
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Null(outcome.ErrorReason);
        Assert.Single(_accounts.Accounts);
        Assert.True(existing.IsActive);
        Assert.Null(existing.REVOKED_REASON);
        Assert.Equal("refresh-token-1", _protector.Decrypt(existing.REFRESH_TOKEN_ENCRYPTED!));
        Assert.Empty(_oauth.RevokedTokens); // the old token was already dead; nothing to retire
    }

    [Fact]
    public async Task CompleteConnect_ReplacingAStillActiveCredential_RetiresTheOldTokenAtGoogle()
    {
        ActiveAccount("old-refresh");
        var state = await BeginAsync();

        await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Equal(["old-refresh"], _oauth.RevokedTokens);
        Assert.Equal("refresh-token-1", _protector.Decrypt(_accounts.Accounts.Single().REFRESH_TOKEN_ENCRYPTED!));
    }

    [Fact]
    public async Task CompleteConnect_Success_ResetsTheInstructorsStuckMeetingsToPending_ButNotUsableOnes()
    {
        var stuckAwaitingLink = SESSION_MEETING.Stage(Guid.NewGuid());
        stuckAwaitingLink.AssignInstructor(InstructorId);
        stuckAwaitingLink.ResolveAsAwaitingLink();

        var stuckNeedsReconnect = SESSION_MEETING.Stage(Guid.NewGuid());
        stuckNeedsReconnect.AssignInstructor(InstructorId);
        stuckNeedsReconnect.RecordNeedsReconnect("invalid_grant");

        var hasUrl = SESSION_MEETING.Stage(Guid.NewGuid());
        hasUrl.AssignInstructor(InstructorId);
        hasUrl.SetManualLink(_protector.Encrypt("https://zoom.us/j/1"));

        var otherInstructors = SESSION_MEETING.Stage(Guid.NewGuid());
        otherInstructors.AssignInstructor(Guid.NewGuid());
        otherInstructors.ResolveAsAwaitingLink();

        _meetings.Meetings.AddRange([stuckAwaitingLink, stuckNeedsReconnect, hasUrl, otherInstructors]);
        var state = await BeginAsync();

        await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Equal(MeetingSyncStatus.Pending, stuckAwaitingLink.SYNC_STATUS);
        Assert.Equal(MeetingSyncStatus.Pending, stuckNeedsReconnect.SYNC_STATUS);
        Assert.Equal(MeetingSyncStatus.Synced, hasUrl.SYNC_STATUS);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, otherInstructors.SYNC_STATUS);
    }

    [Fact]
    public async Task CompleteConnect_Logs_NeverContainTokensCodesStateOrEmail()
    {
        var state = await BeginAsync();
        _oauth.ExchangeResult = Result.Failure<GoogleTokenSet>(GoogleErrors.BadRequest("nope"));

        await Service().CompleteConnectAsync("super-secret-auth-code", state, null, CancellationToken.None);

        var logs = _logger.All;
        Assert.DoesNotContain("super-secret-auth-code", logs);
        Assert.DoesNotContain(state, logs);
        Assert.DoesNotContain("refresh-token-1", logs);
        Assert.DoesNotContain("teacher@gmail.test", logs);
    }

    // ---- Disconnect -------------------------------------------------------------------------------

    [Fact]
    public async Task Disconnect_ActiveAccount_RevokesAtGoogle_ClearsTheStoredToken_AndRecordsUserDisconnected()
    {
        var account = ActiveAccount("refresh-to-revoke");

        await Service().DisconnectAsync(InstructorId, CancellationToken.None);

        Assert.Equal(["refresh-to-revoke"], _oauth.RevokedTokens);
        Assert.Null(account.REFRESH_TOKEN_ENCRYPTED);
        Assert.False(account.IsActive);
        Assert.Equal("user_disconnected", account.REVOKED_REASON);
        Assert.Equal(1, _accounts.SaveCount);
    }

    [Fact]
    public async Task Disconnect_IsIdempotent()
    {
        ActiveAccount();
        var service = Service();

        await service.DisconnectAsync(InstructorId, CancellationToken.None);
        await service.DisconnectAsync(InstructorId, CancellationToken.None);
        await Service().DisconnectAsync(Guid.NewGuid(), CancellationToken.None); // someone who never connected

        Assert.Single(_oauth.RevokedTokens);
        Assert.Equal(1, _accounts.SaveCount);
    }

    [Fact]
    public async Task Disconnect_LeavesExistingRoomsUsable()
    {
        ActiveAccount();
        var meeting = SESSION_MEETING.Stage(Guid.NewGuid());
        meeting.AssignInstructor(InstructorId);
        meeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
        meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt", _protector.Encrypt("https://meet.google.com/x"), Guid.NewGuid(), _clock);
        _meetings.Meetings.Add(meeting);

        await Service().DisconnectAsync(InstructorId, CancellationToken.None);

        Assert.True(meeting.IsUsable);
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
    }

    // ---- Access tokens -----------------------------------------------------------------------------

    [Fact]
    public async Task TryGetAccessToken_NoAccount_IsNotConnected()
    {
        var result = await Service().TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InstructorGoogleAccountService.NotConnectedCode, result.Error.Code);
        Assert.Equal(0, _oauth.RefreshCalls);
    }

    [Fact]
    public async Task TryGetAccessToken_RevokedAccount_IsNotConnected()
    {
        ActiveAccount().MarkRevoked(GoogleAccountRevokedReason.UserDisconnected, _clock);

        var result = await Service().TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.Equal(InstructorGoogleAccountService.NotConnectedCode, result.Error.Code);
    }

    [Fact]
    public async Task TryGetAccessToken_Success_ReturnsTheAccessToken_MarksTheAccountValidated_AndCachesItForTheRun()
    {
        var account = ActiveAccount();
        var service = Service();

        var first = await service.TryGetAccessTokenAsync(InstructorId, CancellationToken.None);
        var second = await service.TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal("access-token-2", first.Value);
        Assert.Equal("access-token-2", second.Value);
        Assert.Equal(1, _oauth.RefreshCalls);
        Assert.Equal(LiveTestData.Now, account.LAST_VALIDATED_AT_UTC);
    }

    [Fact]
    public async Task TryGetAccessToken_ExpiredCachedToken_IsRefreshedAgain()
    {
        ActiveAccount();
        var service = Service();
        await service.TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        _clock.UtcNow = LiveTestData.Now.AddMinutes(61); // past the token's expiry (now + 1h) minus the skew

        await service.TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.Equal(2, _oauth.RefreshCalls);
    }

    [Fact]
    public async Task TryGetAccessToken_InvalidGrant_RevokesTheAccount_AlertsOnce_AndPersistsTheRevocation()
    {
        var account = ActiveAccount();
        _oauth.RefreshResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Unauthorized("dead", "invalid_grant"));
        var service = Service();

        var first = await service.TryGetAccessTokenAsync(InstructorId, CancellationToken.None);
        var second = await service.TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.True(first.IsFailure);
        Assert.Equal(GoogleErrors.UnauthorizedCode, first.Error.Code);
        Assert.Equal(GoogleErrors.UnauthorizedCode, second.Error.Code);
        Assert.False(account.IsActive);
        Assert.Null(account.REFRESH_TOKEN_ENCRYPTED);
        Assert.Equal("invalid_grant", account.REVOKED_REASON);
        Assert.Single(_alerts.ReconnectAlerts);
        Assert.Equal(InstructorId, _alerts.ReconnectAlerts[0].InstructorUserId);
        Assert.Equal(1, _accounts.SaveCount);
        Assert.Equal(1, _oauth.RefreshCalls);
    }

    [Fact]
    public async Task TryGetAccessToken_AfterRevocationInAnotherRun_DoesNotAlertAgain()
    {
        ActiveAccount();
        _oauth.RefreshResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Unauthorized("dead", "invalid_grant"));

        await Service().TryGetAccessTokenAsync(InstructorId, CancellationToken.None);
        var later = await Service().TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.Equal(InstructorGoogleAccountService.NotConnectedCode, later.Error.Code);
        Assert.Single(_alerts.ReconnectAlerts);
    }

    [Theory]
    [InlineData("invalid_client")]
    [InlineData("unauthorized_client")]
    public async Task TryGetAccessToken_ClientCredentialsRejected_DoesNotRevoke_DoesNotAlert_AndIsTreatedAsTransient(string reason)
    {
        var account = ActiveAccount();
        _oauth.RefreshResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Unauthorized("bad client", reason));

        var result = await Service().TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code); // retried by the job, never "reconnect"
        Assert.Equal("invalid_client", result.Error.Reason);
        Assert.True(account.IsActive);
        Assert.NotNull(account.REFRESH_TOKEN_ENCRYPTED);
        Assert.Empty(_alerts.ReconnectAlerts);
        Assert.Equal(0, _accounts.SaveCount);
        Assert.Contains("Integrations:Google", _logger.All); // the operator is told where to look
    }

    [Theory]
    [InlineData(GoogleErrors.TransientCode)]
    [InlineData(GoogleErrors.RateLimitedCode)]
    [InlineData(GoogleErrors.BadRequestCode)]
    public async Task TryGetAccessToken_TransientGoogleErrors_KeepTheAccountAndPassTheErrorThrough(string code)
    {
        var account = ActiveAccount();
        _oauth.RefreshResult = Result.Failure<GoogleTokenSet>(new DomainError(code, "x"));

        var result = await Service().TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.Equal(code, result.Error.Code);
        Assert.True(account.IsActive);
        Assert.Empty(_alerts.ReconnectAlerts);
    }

    /// <summary>D1 (integrator-qa): a ciphertext this host cannot read is OUR configuration problem (wrong/rotated key, a Workers container
    /// started without the real key, a corrupt column) — it says nothing about Google, so it must never revoke the connection or e-mail the
    /// instructor (before the fix every instructor of a misconfigured Workers host was disconnected and told to reconnect).</summary>
    [Fact]
    public async Task TryGetAccessToken_UnreadableStoredToken_IsATransientPlatformProblem_NeverRevokesOrAlerts()
    {
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            InstructorId, "sub", "t@gmail.test", LiveTestData.Protector().Encrypt("encrypted-with-another-key"), GoogleScopes.CalendarEventsOwned, _clock);
        _accounts.Accounts.Add(account);

        var result = await Service().TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code); // retried by the job, never "reconnect"
        Assert.Equal(InstructorGoogleAccountService.CredentialUnreadableReason, result.Error.Reason);
        Assert.True(account.IsActive);
        Assert.NotNull(account.REFRESH_TOKEN_ENCRYPTED);
        Assert.Null(account.REVOKED_REASON);
        Assert.Empty(_alerts.ReconnectAlerts);
        Assert.Equal(0, _accounts.SaveCount);
        Assert.Equal(0, _oauth.RefreshCalls);
        Assert.Empty(_oauth.RevokedTokens);
        Assert.Contains("DataProtection:EncryptionKeyBase64", _logger.All); // the operator is told where to look
    }

    [Theory]
    [InlineData("this is not base64 !!!")] // FormatException
    [InlineData("AAAA")] // 3 bytes — too short to hold a nonce and tag: InvalidOperationException from SensitiveDataProtector
    public async Task TryGetAccessToken_OtherUnreadableCiphertextShapes_AreTreatedTheSame(string brokenCiphertext)
    {
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(InstructorId, "sub", "t@gmail.test", brokenCiphertext, GoogleScopes.CalendarEventsOwned, _clock);
        _accounts.Accounts.Add(account);

        var result = await Service().TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
        Assert.Equal(InstructorGoogleAccountService.CredentialUnreadableReason, result.Error.Reason);
        Assert.True(account.IsActive);
        Assert.Empty(_alerts.ReconnectAlerts);
        Assert.Equal(0, _accounts.SaveCount);
        Assert.DoesNotContain(brokenCiphertext, _logger.All);
    }

    [Fact]
    public async Task TryGetAccessToken_UnreadableStoredToken_OnEveryRun_NeverEscalatesToARevocation()
    {
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            InstructorId, "sub", "t@gmail.test", LiveTestData.Protector().Encrypt("encrypted-with-another-key"), GoogleScopes.CalendarEventsOwned, _clock);
        _accounts.Accounts.Add(account);

        for (var run = 0; run < 3; run++)
        {
            var result = await Service().TryGetAccessTokenAsync(InstructorId, CancellationToken.None);
            Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
        }

        Assert.True(account.IsActive);
        Assert.Empty(_alerts.ReconnectAlerts);
        Assert.Equal(0, _accounts.SaveCount);
    }

    /// <summary>The recovery path for a genuinely corrupt row: the instructor disconnects (and reconnects). A truncated ciphertext used to throw
    /// InvalidOperationException out of the best-effort token retirement, turning "Disconnect" into a 500.</summary>
    [Fact]
    public async Task Disconnect_WithATruncatedStoredToken_StillDisconnects_WithoutRetiringAnythingAtGoogle()
    {
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(InstructorId, "sub", "t@gmail.test", "AAAA", GoogleScopes.CalendarEventsOwned, _clock);
        _accounts.Accounts.Add(account);

        await Service().DisconnectAsync(InstructorId, CancellationToken.None);

        Assert.False(account.IsActive);
        Assert.Equal("user_disconnected", account.REVOKED_REASON);
        Assert.Empty(_oauth.RevokedTokens);
        Assert.Equal(1, _accounts.SaveCount);
    }

    [Fact]
    public async Task CompleteConnect_ReplacingAnUnreadableActiveCredential_Succeeds()
    {
        var existing = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(InstructorId, "sub", "t@gmail.test", "AAAA", GoogleScopes.CalendarEventsOwned, _clock);
        _accounts.Accounts.Add(existing);
        var state = await BeginAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Null(outcome.ErrorReason);
        Assert.Equal("refresh-token-1", _protector.Decrypt(existing.REFRESH_TOKEN_ENCRYPTED!));
        Assert.True(existing.IsActive);
    }

    [Theory]
    [InlineData("insufficientPermissions", GoogleAccountRevokedReason.InsufficientScope)]
    [InlineData("ACCESS_TOKEN_SCOPE_INSUFFICIENT", GoogleAccountRevokedReason.InsufficientScope)]
    [InlineData("forbidden", GoogleAccountRevokedReason.InsufficientScope)]
    [InlineData(null, GoogleAccountRevokedReason.InvalidGrant)]
    [InlineData("authError", GoogleAccountRevokedReason.InvalidGrant)]
    public async Task HandleCalendarUnauthorized_RevokesWithTheRightReason_AndAlertsOnce(string? googleReason, string expectedRevokedReason)
    {
        var account = ActiveAccount();
        var error = GoogleErrors.Unauthorized("calendar said no", googleReason);
        var service = Service();

        var reason = await service.HandleCalendarUnauthorizedAsync(InstructorId, error, CancellationToken.None);
        await service.HandleCalendarUnauthorizedAsync(InstructorId, error, CancellationToken.None);

        Assert.Equal(expectedRevokedReason, reason);
        Assert.Equal(expectedRevokedReason, account.REVOKED_REASON);
        Assert.Single(_alerts.ReconnectAlerts);
    }

    [Fact]
    public async Task TryGetAccessToken_Logs_NeverContainTokens()
    {
        ActiveAccount("super-secret-refresh");
        _oauth.RefreshResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Unauthorized("dead", "invalid_grant"));

        await Service().TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.DoesNotContain("super-secret-refresh", _logger.All);
    }

    private static void AssertError(GoogleConnectOutcome outcome, string reason, string path)
    {
        Assert.Equal(reason, outcome.ErrorReason);
        Assert.Equal($"https://app.example.test{path}?google=error&reason={reason}", outcome.RedirectUrl);
    }

    private sealed class ThrowingProtector : ISensitiveDataProtector
    {
        public string Encrypt(string plainText) => throw new InvalidOperationException("boom");

        public string Decrypt(string cipherTextBase64) => throw new InvalidOperationException("boom");

        public string MaskAccountNumber(string? accountNo) => throw new NotSupportedException();
    }
}
