using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>P11-13 on the Google connection: the account kind is detected from userinfo's <c>hd</c> (never asked) at every connect and, once, lazily for old rows; the status reports it
/// together with the recording capability; and the second, optional "recording access" consent has its own purpose-bound state and insists on both recording scopes.</summary>
public class InstructorGoogleAccountRecordingTests
{
    private static readonly Guid InstructorId = Guid.NewGuid();
    private const string PostConnectPath = "/instructor/live-settings";
    private const string WorkspaceDomain = "school.example.test";

    private static readonly string AllScopes = string.Join(' ', GoogleScopes.OpenId, GoogleScopes.Email, GoogleScopes.CalendarEventsOwned, GoogleScopes.MeetSpaceReadonly, GoogleScopes.DriveMeetReadonly);

    private readonly InMemoryAccountRepository _accounts = new();
    private readonly InMemorySessionMeetingRepository _meetings = new();
    private readonly FakeGoogleOAuth _oauth = new();
    private readonly FakeStateStore _states = new();
    private readonly FakeSchedule _schedule = new();
    private readonly RecordingAlertSender _alerts = new();
    private readonly FakeClock _clock = new(LiveTestData.Now);
    private readonly ISensitiveDataProtector _protector = LiveTestData.Protector();
    private readonly ListLogger<InstructorGoogleAccountService> _logger = new();
    private bool _enabled = true;
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
        LiveTestData.OptionsOf(o =>
        {
            o.Provider = _mode;
            o.Recording.AutoImport.Enabled = _enabled;
        }),
        Microsoft.Extensions.Options.Options.Create(new GoogleOAuthOptions { PostConnectRedirectPath = PostConnectPath }),
        _logger);

    private INSTRUCTOR_GOOGLE_ACCOUNT Account(string? hostedDomain = WorkspaceDomain, string? scopes = null, bool checkedKind = true)
    {
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            InstructorId, "sub-1", "teacher@school.example.test", _protector.Encrypt("refresh-token-1"), scopes ?? GoogleScopes.CalendarEventsOwned, _clock, hostedDomain);

        if (!checkedKind)
        {
            typeof(INSTRUCTOR_GOOGLE_ACCOUNT).GetProperty(nameof(INSTRUCTOR_GOOGLE_ACCOUNT.ACCOUNT_KIND_CHECKED_AT_UTC))!.GetSetMethod(nonPublic: true)!.Invoke(account, [null]);
            typeof(INSTRUCTOR_GOOGLE_ACCOUNT).GetProperty(nameof(INSTRUCTOR_GOOGLE_ACCOUNT.HOSTED_DOMAIN))!.GetSetMethod(nonPublic: true)!.Invoke(account, [null]);
        }

        _accounts.Accounts.Add(account);
        return account;
    }

    private static Result<GoogleUserInfo> UserInfo(string? hd) => Result.Success(new GoogleUserInfo("sub-1", "teacher@school.example.test", true, hd));

    // ---- Status: account kind and capability -------------------------------------------------------------

    [Fact]
    public async Task Status_NotConnected_HasNoAccountKind_AndTheFlagIsReported()
    {
        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Null(status.AccountKind);
        Assert.Null(status.HostedDomain);
        Assert.Equal(new RecordingCapabilityInfo(RecordingCapability.Manual, AutoImportAvailable: true, ScopesGranted: false), status.Recording);
    }

    [Fact]
    public async Task Status_FeatureOff_IsManual_AndSaysTheFeatureIsNotAvailable()
    {
        _enabled = false;
        Account(scopes: AllScopes);

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Equal(GoogleAccountKind.Workspace, status.AccountKind);
        Assert.Equal(new RecordingCapabilityInfo(RecordingCapability.Manual, AutoImportAvailable: false, ScopesGranted: true), status.Recording);
    }

    [Fact]
    public async Task Status_WorkspaceWithoutRecordingAccess_OffersTheConsent()
    {
        Account();

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Equal(GoogleAccountKind.Workspace, status.AccountKind);
        Assert.Equal(WorkspaceDomain, status.HostedDomain);
        Assert.Equal(new RecordingCapabilityInfo(RecordingCapability.AutoNeedsConsent, true, false), status.Recording);
    }

    [Fact]
    public async Task Status_WorkspaceWithRecordingAccess_IsAuto()
    {
        Account(scopes: AllScopes);

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Equal(new RecordingCapabilityInfo(RecordingCapability.Auto, true, true), status.Recording);
    }

    [Fact]
    public async Task Status_PersonalAccount_IsManual_AndHasNoDomain()
    {
        Account(hostedDomain: null, scopes: AllScopes);

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Equal(GoogleAccountKind.Personal, status.AccountKind);
        Assert.Null(status.HostedDomain);
        Assert.Equal(RecordingCapability.Manual, status.Recording.Mode);
    }

    [Fact]
    public async Task Status_ARevokedAccount_ShowsNoKind_AndIsManual()
    {
        Account(scopes: AllScopes).MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, _clock);

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Null(status.AccountKind);
        Assert.Null(status.HostedDomain);
        Assert.Equal(RecordingCapability.Manual, status.Recording.Mode);
    }

    [Fact]
    public async Task Status_AnOldRow_HasItsKindResolvedOnceFromUserinfo_AndPersisted()
    {
        var account = Account(checkedKind: false);
        _oauth.UserInfoResult = UserInfo("School.Example.Test");

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Equal(GoogleAccountKind.Workspace, status.AccountKind);
        Assert.Equal(WorkspaceDomain, status.HostedDomain);
        Assert.Equal(LiveTestData.Now, account.ACCOUNT_KIND_CHECKED_AT_UTC);
        Assert.Equal(1, _accounts.SaveCount);

        // A second read does not ask Google again.
        var refreshesBefore = _oauth.RefreshCalls;
        await Service().GetStatusAsync(InstructorId, CancellationToken.None);
        Assert.Equal(refreshesBefore, _oauth.RefreshCalls);
        Assert.Equal(1, _accounts.SaveCount);
    }

    [Fact]
    public async Task Status_FeatureOff_NeverAsksGoogleForTheKind_NotEvenForAnOldRow()
    {
        _enabled = false;
        var account = Account(checkedKind: false);
        _oauth.UserInfoResult = UserInfo(WorkspaceDomain); // would resolve, if it were ever asked

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.True(status.Connected);
        Assert.Equal(GoogleAccountKind.Unknown, status.AccountKind); // the stored value: never looked at
        Assert.Equal(new RecordingCapabilityInfo(RecordingCapability.Manual, AutoImportAvailable: false, ScopesGranted: false), status.Recording);
        Assert.Null(account.ACCOUNT_KIND_CHECKED_AT_UTC);
        Assert.Equal(0, _oauth.RefreshCalls); // no token refresh...
        Assert.Equal(0, _oauth.UserInfoCalls); // ...and no userinfo call
        Assert.Equal(0, _accounts.SaveCount);
    }

    [Fact]
    public async Task ResolveAccountKind_FeatureOff_ReturnsTheStoredKind_WithoutAnyOutboundCall()
    {
        _enabled = false;
        var old = Account(checkedKind: false);
        var known = Account(hostedDomain: null);
        _accounts.Accounts.Remove(known); // a second, separately built account object (the same instructor id is irrelevant: the repository is not consulted)

        Assert.Equal(GoogleAccountKind.Unknown, await Service().TryResolveAccountKindAsync(old, CancellationToken.None));
        Assert.Equal(GoogleAccountKind.Personal, await Service().TryResolveAccountKindAsync(known, CancellationToken.None));
        Assert.Equal(0, _oauth.RefreshCalls);
        Assert.Equal(0, _oauth.UserInfoCalls);
    }

    [Fact]
    public async Task Status_FeatureOn_StillResolvesAnOldRow()
    {
        Account(checkedKind: false);
        _oauth.UserInfoResult = UserInfo(WorkspaceDomain);

        await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Equal(1, _oauth.UserInfoCalls);
    }

    [Fact]
    public async Task Status_AnOldPersonalRow_IsResolvedAsPersonal()
    {
        Account(checkedKind: false);
        _oauth.UserInfoResult = UserInfo(null);

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Equal(GoogleAccountKind.Personal, status.AccountKind);
    }

    [Fact]
    public async Task Status_WhenTheLookupFails_TheKindStaysUnknown_AndTheStatusStillAnswers()
    {
        var account = Account(checkedKind: false);
        _oauth.UserInfoResult = Result.Failure<GoogleUserInfo>(GoogleErrors.Transient("down"));

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.True(status.Connected);
        Assert.Equal(GoogleAccountKind.Unknown, status.AccountKind);
        Assert.Null(account.ACCOUNT_KIND_CHECKED_AT_UTC);
        Assert.Equal(RecordingCapability.Manual, status.Recording.Mode);
        Assert.Equal(0, _accounts.SaveCount);
    }

    [Fact]
    public async Task Status_WhenNoTokenCanBeObtained_TheKindStaysUnknown_NothingThrows()
    {
        Account(checkedKind: false);
        _oauth.RefreshResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Transient("down"));

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Equal(GoogleAccountKind.Unknown, status.AccountKind);
    }

    [Fact]
    public async Task Status_GoogleNotConfigured_DoesNotTryToResolveTheKind()
    {
        _oauth.Configured = false;
        Account(checkedKind: false);

        var status = await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.Equal(GoogleAccountKind.Unknown, status.AccountKind);
        Assert.Equal(0, _oauth.RefreshCalls);
    }

    // ---- Recording-access connect ------------------------------------------------------------------------

    [Fact]
    public async Task BeginRecordingAccess_ForAConnectedWorkspaceAccount_StoresARecordingAccessState_AndReturnsTheRecordingConsentUrl()
    {
        Account();

        var result = await Service().BeginRecordingAccessConnectAsync(InstructorId, "/instructor/live-settings", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("https://accounts.example.test/auth-recording", result.Value.AuthorizationUrl);
        var stored = Assert.Single(_states.States).Value;
        Assert.Equal(GoogleOAuthPurpose.RecordingAccess, stored.Purpose);
        Assert.Equal(InstructorId, stored.UserId);
        Assert.Equal(PostConnectPath, stored.ReturnPath);
        Assert.Equal(TimeSpan.FromMinutes(10), _states.LastTimeToLive);
    }

    [Fact]
    public async Task BeginRecordingAccess_TheOrdinaryConnectStillCarriesTheCalendarPurpose()
    {
        var result = await Service().BeginConnectAsync(InstructorId, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(GoogleOAuthPurpose.Calendar, Assert.Single(_states.States).Value.Purpose);
    }

    [Fact]
    public async Task BeginRecordingAccess_FeatureOff_Is409NotAvailable()
    {
        _enabled = false;
        Account();

        var result = await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None);

        AssertNotAvailable(result);
        Assert.Empty(_states.States);
    }

    [Fact]
    public async Task BeginRecordingAccess_NoAccountOrARevokedOne_Is409NotAvailable()
    {
        AssertNotAvailable(await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None));

        Account().MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, _clock);
        AssertNotAvailable(await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None));
        Assert.Empty(_states.States);
    }

    [Fact]
    public async Task BeginRecordingAccess_PersonalAccount_Is409NotAvailable()
    {
        Account(hostedDomain: null);

        AssertNotAvailable(await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None));
        Assert.Empty(_states.States);
    }

    [Fact]
    public async Task BeginRecordingAccess_AnOldRow_IsLookedUpFirst()
    {
        Account(checkedKind: false);
        _oauth.UserInfoResult = UserInfo(WorkspaceDomain);

        var result = await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task BeginRecordingAccess_AnOldRowWhoseKindCannotBeResolved_Is409NotAvailable()
    {
        Account(checkedKind: false);
        _oauth.UserInfoResult = Result.Failure<GoogleUserInfo>(GoogleErrors.Transient("down"));

        AssertNotAvailable(await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None));
    }

    [Fact]
    public async Task BeginRecordingAccess_GoogleNotConfigured_Is503LikeTheCalendarConnect()
    {
        _oauth.Configured = false;
        Account();

        var result = await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unavailable", result.Error.Code);
        Assert.Equal(InstructorGoogleAccountService.NotConfiguredReason, result.Error.Reason);
    }

    [Fact]
    public async Task BeginRecordingAccess_ManualOnlyRooms_Is503NotConfigured()
    {
        _mode = LiveProviderMode.ManualOnly;
        Account();

        var result = await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None);

        Assert.Equal("unavailable", result.Error.Code);
    }

    [Fact]
    public async Task BeginRecordingAccess_StateStoreDown_FailsClosedWith503()
    {
        Account();
        _states.Available = false;

        var result = await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unavailable", result.Error.Code);
    }

    [Fact]
    public async Task BeginRecordingAccess_AnOAuthServiceThatCannotAskForRecordingScopes_Is409NotAvailable_NotACrash()
    {
        Account();
        _oauth.RecordingAccessSupported = false;

        AssertNotAvailable(await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None));
        Assert.Empty(_states.States);
    }

    private static void AssertNotAvailable(Result<GoogleConnectResponse> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(LiveReasons.RecordingNotAvailable, result.Error.Reason);
    }

    // ---- Callback: the hosted domain at every connect -----------------------------------------------------

    private async Task<string> BeginCalendarAsync()
    {
        await Service().BeginConnectAsync(InstructorId, null, CancellationToken.None);
        return _oauth.LastState!;
    }

    private async Task<string> BeginRecordingAsync()
    {
        var result = await Service().BeginRecordingAccessConnectAsync(InstructorId, null, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return _oauth.LastRecordingAccessState!;
    }

    [Fact]
    public async Task Connect_StoresTheHostedDomain_ForAWorkspaceAccount()
    {
        _oauth.UserInfoResult = UserInfo("School.Example.Test");
        var state = await BeginCalendarAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Null(outcome.ErrorReason);
        var account = Assert.Single(_accounts.Accounts);
        Assert.Equal(WorkspaceDomain, account.HOSTED_DOMAIN);
        Assert.Equal(GoogleAccountKind.Workspace, account.AccountKind);
        Assert.Equal(LiveTestData.Now, account.ACCOUNT_KIND_CHECKED_AT_UTC);
    }

    [Fact]
    public async Task Connect_AGmailAccount_IsRecordedAsPersonal_NotUnknown()
    {
        _oauth.UserInfoResult = UserInfo(null);
        var state = await BeginCalendarAsync();

        await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        var account = Assert.Single(_accounts.Accounts);
        Assert.Null(account.HOSTED_DOMAIN);
        Assert.Equal(GoogleAccountKind.Personal, account.AccountKind);
    }

    [Fact]
    public async Task Reconnect_RefreshesTheHostedDomain_SoSwitchingAccountsIsNoticed()
    {
        var existing = Account();
        _oauth.UserInfoResult = UserInfo(null); // reconnected with a Gmail account this time
        var state = await BeginCalendarAsync();

        await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Equal(GoogleAccountKind.Personal, existing.AccountKind);
        Assert.Null(existing.HOSTED_DOMAIN);
    }

    // ---- Callback: the recording-access purpose ---------------------------------------------------------------

    [Fact]
    public async Task RecordingAccessCallback_WithBothRecordingScopes_StoresTheNewGrant()
    {
        var existing = Account();
        _oauth.UserInfoResult = UserInfo(WorkspaceDomain);
        _oauth.ExchangeResult = Result.Success(new GoogleTokenSet("access-r", LiveTestData.Now.AddHours(1), "refresh-r", AllScopes));
        var state = await BeginRecordingAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Null(outcome.ErrorReason);
        Assert.Equal($"https://app.example.test{PostConnectPath}?google=connected", outcome.RedirectUrl);
        Assert.Same(existing, Assert.Single(_accounts.Accounts));
        Assert.True(existing.HasRecordingScopes);
        Assert.Equal(AllScopes, existing.SCOPES);
        Assert.Equal("refresh-r", _protector.Decrypt(existing.REFRESH_TOKEN_ENCRYPTED!));
        Assert.Equal(GoogleAccountKind.Workspace, existing.AccountKind);
    }

    [Fact]
    public async Task RecordingAccessCallback_DoesNotRetireTheOldTokenAtGoogle_BecauseItSharesTheGrant()
    {
        Account();
        _oauth.UserInfoResult = UserInfo(WorkspaceDomain);
        _oauth.ExchangeResult = Result.Success(new GoogleTokenSet("access-r", LiveTestData.Now.AddHours(1), "refresh-r", AllScopes));
        var state = await BeginRecordingAsync();

        await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Empty(_oauth.RevokedTokens); // revoking it would have revoked the grant behind the new token too
    }

    [Theory]
    [InlineData("https://www.googleapis.com/auth/meetings.space.readonly")] // only one of the two
    [InlineData("https://www.googleapis.com/auth/drive.meet.readonly")]
    [InlineData("")]
    public async Task RecordingAccessCallback_WithoutBothRecordingScopes_IsRecordingScopeMissing_AndStoresNothing(string extraScope)
    {
        var existing = Account();
        var originalToken = existing.REFRESH_TOKEN_ENCRYPTED;
        _oauth.ExchangeResult = Result.Success(new GoogleTokenSet(
            "access-r", LiveTestData.Now.AddHours(1), "refresh-r", $"openid email {GoogleScopes.CalendarEventsOwned} {extraScope}".Trim()));
        var state = await BeginRecordingAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Equal("recording_scope_missing", outcome.ErrorReason);
        Assert.Equal($"https://app.example.test{PostConnectPath}?google=error&reason=recording_scope_missing", outcome.RedirectUrl);
        Assert.Equal(originalToken, existing.REFRESH_TOKEN_ENCRYPTED); // the working connection is untouched
        Assert.False(existing.HasRecordingScopes);
        Assert.Equal(0, _accounts.SaveCount);
        Assert.Empty(_oauth.RevokedTokens); // not revoked: the same grant is still in use for the calendar
    }

    [Fact]
    public async Task RecordingAccessCallback_StillRequiresTheCalendarScope()
    {
        Account();
        _oauth.ExchangeResult = Result.Success(new GoogleTokenSet(
            "access-r", LiveTestData.Now.AddHours(1), "refresh-r", $"openid email {GoogleScopes.MeetSpaceReadonly} {GoogleScopes.DriveMeetReadonly}"));
        var state = await BeginRecordingAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Equal("scope_missing", outcome.ErrorReason);
    }

    [Fact]
    public async Task CalendarCallback_DoesNotInsistOnTheRecordingScopes()
    {
        _oauth.ExchangeResult = Result.Success(new GoogleTokenSet("a", LiveTestData.Now.AddHours(1), "r", $"openid email {GoogleScopes.CalendarEventsOwned}"));
        var state = await BeginCalendarAsync();

        var outcome = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Null(outcome.ErrorReason);
        Assert.False(Assert.Single(_accounts.Accounts).HasRecordingScopes);
    }

    [Fact]
    public async Task RecordingAccessCallback_AReplayedState_IsRefused()
    {
        Account();
        _oauth.ExchangeResult = Result.Success(new GoogleTokenSet("a", LiveTestData.Now.AddHours(1), "r", AllScopes));
        var state = await BeginRecordingAsync();

        await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);
        var replay = await Service().CompleteConnectAsync("code", state, null, CancellationToken.None);

        Assert.Equal("state_invalid", replay.ErrorReason);
    }

    [Fact]
    public async Task RecordingAccessCallback_UserDeniedConsent_IsAccessDenied_OnTheReturnPath()
    {
        Account();
        var state = await BeginRecordingAsync();

        var outcome = await Service().CompleteConnectAsync(null, state, "access_denied", CancellationToken.None);

        Assert.Equal("access_denied", outcome.ErrorReason);
    }

    [Fact]
    public async Task NoLog_ContainsAnythingSecret_DuringTheRecordingConsent()
    {
        Account();
        _oauth.UserInfoResult = UserInfo(WorkspaceDomain);
        _oauth.ExchangeResult = Result.Success(new GoogleTokenSet("access-r", LiveTestData.Now.AddHours(1), "refresh-r", AllScopes));
        var state = await BeginRecordingAsync();

        await Service().CompleteConnectAsync("secret-auth-code", state, null, CancellationToken.None);
        await Service().GetStatusAsync(InstructorId, CancellationToken.None);

        Assert.DoesNotContain("secret-auth-code", _logger.All);
        Assert.DoesNotContain("refresh-r", _logger.All);
        Assert.DoesNotContain("access-r", _logger.All);
        Assert.DoesNotContain(WorkspaceDomain, _logger.All);
        Assert.DoesNotContain("teacher@", _logger.All);
    }
}
