using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Siri.Integrations.Google;
using Siri.Integrations.Google.Logging;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>
/// Every row of the P11-03 contract's sync-job table (section 6.3) with fake Google + a fake clock: manual fallback, Google success, pending
/// conferences, backoff to Failed, expired tokens, deleted events, orphan adoption, error isolation — plus "no secret in any log".
/// </summary>
public class LiveMeetingSyncJobTests
{
    private const string GoodMeetUrl = "https://meet.google.com/abc-defg-hij";
    private static readonly Guid InstructorId = Guid.NewGuid();

    private readonly InMemorySessionMeetingRepository _meetings = new();
    private readonly InMemoryAccountRepository _accounts = new();
    private readonly FakeGoogleOAuth _oauth = new();
    private readonly FakeSchedule _schedule = new();
    private readonly RecordingAlertSender _alerts = new();
    private readonly FakeCalendar _calendar = new();
    private readonly FakeClock _clock = new(LiveTestData.Now);
    private readonly ISensitiveDataProtector _protector = LiveTestData.Protector();
    private readonly ListLogger<LiveMeetingSyncJob> _jobLog = new();
    private readonly ListLogger<InstructorGoogleAccountService> _serviceLog = new();
    private LiveProviderMode _mode = LiveProviderMode.GoogleMeet;

    private LiveMeetingSyncJob Job()
    {
        var options = LiveTestData.OptionsOf(o => o.Provider = _mode);
        var googleAccounts = new InstructorGoogleAccountService(
            _accounts,
            _meetings,
            _oauth,
            new FakeStateStore(),
            _protector,
            _schedule,
            _alerts,
            _clock,
            options,
            Microsoft.Extensions.Options.Options.Create(new GoogleOAuthOptions()),
            _serviceLog);

        return new LiveMeetingSyncJob(
            _meetings,
            _schedule,
            googleAccounts,
            _calendar,
            new MeetingLinkValidator(options),
            _alerts,
            _protector,
            _clock,
            options,
            _jobLog)
        {
            ConferencePollInterval = TimeSpan.Zero,
        };
    }

    private INSTRUCTOR_GOOGLE_ACCOUNT ConnectedAccount(string refreshToken = "refresh-token-1")
    {
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(InstructorId, "sub", "teacher@gmail.test", _protector.Encrypt(refreshToken), GoogleScopes.CalendarEventsOwned, _clock);
        _accounts.Accounts.Add(account);
        return account;
    }

    private (LiveSessionContext Context, SESSION_MEETING Meeting) PendingSession(Action<SESSION_MEETING>? shape = null, LiveSessionContext? context = null)
    {
        var ctx = context ?? LiveTestData.Context(instructorUserId: InstructorId, description: "รายละเอียดคาบ");
        _schedule.Contexts.Add(ctx);
        var meeting = SESSION_MEETING.Stage(ctx.SessionId);
        shape?.Invoke(meeting);
        _meetings.Meetings.Add(meeting);
        return (ctx, meeting);
    }

    private async Task RunAsync() => await Job().RunAsync(CancellationToken.None);

    private static void Force(SESSION_MEETING meeting, MeetingSyncStatus status) =>
        typeof(SESSION_MEETING).GetProperty(nameof(SESSION_MEETING.SYNC_STATUS), BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(nonPublic: true)!.Invoke(meeting, [status]);

    // ---- No Google account -------------------------------------------------------------------------

    [Fact]
    public async Task Pending_NoAccountAndNoUrl_WaitsForAManualLink_AndTellsTheInstructorOnce()
    {
        var (context, meeting) = PendingSession();

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Equal(MeetingProvider.Manual, meeting.PROVIDER);
        Assert.Equal(InstructorId, meeting.INSTRUCTOR_USER_ID);
        Assert.NotNull(meeting.MEETING_ALERT_SENT_AT_UTC);
        Assert.Equal([(InstructorId, context.SessionId)], _alerts.NeedsLinkAlerts);
        Assert.Empty(_calendar.Calls);

        // Back to Pending (e.g. after a resync): the alert is not repeated.
        meeting.RequestResync();
        await RunAsync();

        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Single(_alerts.NeedsLinkAlerts);
    }

    [Fact]
    public async Task Pending_ManualOnlyMode_NeverCallsGoogle_EvenWithAConnectedAccount()
    {
        _mode = LiveProviderMode.ManualOnly;
        ConnectedAccount();
        var (_, meeting) = PendingSession();

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Empty(_calendar.Calls);
        Assert.Equal(0, _oauth.RefreshCalls);
    }

    [Fact]
    public async Task Pending_NoAccountButAManualUrlAlreadyExists_IsSyncedNotAwaitingLink()
    {
        var (_, meeting) = PendingSession(m => m.SetManualLink(_protector.Encrypt("https://zoom.us/j/1")));
        Force(meeting, MeetingSyncStatus.Pending); // a manual link that outlived its Google account, queued for another pass

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.True(meeting.IsUsable);
        Assert.Empty(_alerts.NeedsLinkAlerts);
        Assert.Empty(_calendar.Calls);
    }

    [Fact]
    public async Task Pending_GoogleEventExistsButTheAccountIsGone_NeedsReconnect_KeepingTheOldUrlUsable()
    {
        var (_, meeting) = PendingSession(m =>
        {
            m.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
            m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-old", _protector.Encrypt(GoodMeetUrl), Guid.NewGuid(), _clock);
            m.MarkSessionChanged(); // time moved: needs a patch
        });

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.NeedsReconnect, meeting.SYNC_STATUS);
        Assert.Equal(MeetingErrorCodes.GoogleAccountUnavailable, meeting.ERROR);
        Assert.True(meeting.IsUsable);
        Assert.Empty(_calendar.Calls);
    }

    // ---- Google success --------------------------------------------------------------------------

    [Fact]
    public async Task Pending_ConnectedAccount_CreatesTheEventWithMeet_AndStoresTheRoomEncrypted()
    {
        var account = ConnectedAccount();
        var (context, meeting) = PendingSession();

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.Equal(MeetingProvider.GoogleMeet, meeting.PROVIDER);
        Assert.Equal(account.INSTRUCTOR_GOOGLE_ACCOUNT_ID, meeting.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
        Assert.Equal("evt-created", meeting.PROVIDER_EVENT_ID);
        Assert.Equal(LiveTestData.Now, meeting.LAST_SYNC_AT_UTC);
        Assert.True(meeting.IsUsable);
        Assert.NotEqual(GoodMeetUrl, meeting.MEET_URL_ENCRYPTED);
        Assert.DoesNotContain("meet.google.com", meeting.MEET_URL_ENCRYPTED);
        Assert.Equal(GoodMeetUrl, _protector.Decrypt(meeting.MEET_URL_ENCRYPTED!));
        Assert.Null(meeting.ERROR);
        Assert.Equal(1, _meetings.SaveCount);

        var create = Assert.Single(_calendar.Calls, c => c.Operation == "create");
        Assert.Equal("access-token-2", create.AccessToken);
        Assert.Equal($"{context.CourseTitle} — {context.Title}", create.Request!.Summary);
        Assert.Equal(context.StartsAtUtc, create.Request.StartsAtUtc);
        Assert.Equal(context.EndsAtUtc, create.Request.EndsAtUtc);
        Assert.Equal(meeting.SESSION_MEETING_ID.ToString("N"), create.Request.RequestId);
        Assert.Equal(context.SessionId.ToString("N"), create.Request.PrivateSessionId);
    }

    [Fact]
    public async Task EventDescription_HasThePlatformJoinLink_AndNeverTheMeetUrlOrAnyone()
    {
        ConnectedAccount();
        var (context, _) = PendingSession(context: LiveTestData.Context(instructorUserId: InstructorId, description: "ทบทวนบทที่ 1"));

        await RunAsync();

        var description = _calendar.Calls.Single(c => c.Operation == "create").Request!.Description!;
        Assert.StartsWith($"เข้าห้องเรียนผ่านแพลตฟอร์ม: https://app.example.test/live/{context.SessionId:D}/join", description);
        Assert.Contains("ทบทวนบทที่ 1", description);
        Assert.DoesNotContain("meet.google.com", description);
        Assert.DoesNotContain("@", description);
    }

    [Fact]
    public async Task Pending_ThenSynced_SecondRunDoesNothingMore()
    {
        ConnectedAccount();
        PendingSession();

        await RunAsync();
        var callsAfterFirst = _calendar.Calls.Count;
        await RunAsync();

        Assert.Equal(callsAfterFirst, _calendar.Calls.Count);
    }

    [Fact]
    public async Task Pending_WithAKnownEvent_PatchesItInsteadOfCreatingAnother_AndKeepsTheRoom()
    {
        var account = ConnectedAccount();
        var (_, meeting) = PendingSession(m =>
        {
            m.AssignProvider(MeetingProvider.GoogleMeet, account.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
            m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-1", _protector.Encrypt(GoodMeetUrl), account.INSTRUCTOR_GOOGLE_ACCOUNT_ID, _clock);
            m.MarkSessionChanged();
        });
        _calendar.OnUpdate = (_, _) => Result.Success(new CalendarEventResult("evt-1", null, ConferencePending: false)); // a patch that reports no room

        await RunAsync();

        Assert.DoesNotContain(_calendar.Calls, c => c.Operation == "create");
        Assert.Single(_calendar.Calls, c => c.Operation == "update" && c.EventId == "evt-1");
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.Equal(GoodMeetUrl, _protector.Decrypt(meeting.MEET_URL_ENCRYPTED!)); // the existing room is kept
    }

    [Fact]
    public async Task Pending_PatchAnswers404_TheEventIsRecreatedInTheSameRun_WithAFreshRequestId()
    {
        var account = ConnectedAccount();
        var (_, meeting) = PendingSession(m =>
        {
            m.AssignProvider(MeetingProvider.GoogleMeet, account.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
            m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-deleted-by-user", _protector.Encrypt("https://meet.google.com/old-old-old"), account.INSTRUCTOR_GOOGLE_ACCOUNT_ID, _clock);
            m.MarkSessionChanged();
        });
        _calendar.OnUpdate = (_, _) => Result.Failure<CalendarEventResult>(GoogleErrors.NotFound("gone"));

        await RunAsync();

        var create = Assert.Single(_calendar.Calls, c => c.Operation == "create");
        Assert.NotEqual(meeting.SESSION_MEETING_ID.ToString("N"), create.Request!.RequestId);
        Assert.StartsWith(meeting.SESSION_MEETING_ID.ToString("N"), create.Request.RequestId);
        Assert.Equal("evt-created", meeting.PROVIDER_EVENT_ID);
        Assert.Equal(GoodMeetUrl, _protector.Decrypt(meeting.MEET_URL_ENCRYPTED!));
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
    }

    [Fact]
    public async Task Pending_AfterAFailedAttempt_LooksForAnEventAFirstCallMayHaveCreated_AndAdoptsItInsteadOfDuplicating()
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession(m => m.RecordAttemptFailed(MeetingErrorCodes.GoogleTransient, _clock));
        _clock.UtcNow = LiveTestData.Now.AddMinutes(2); // past the 1 minute backoff
        _calendar.OnFind = _ => Result.Success<CalendarEventResult?>(new CalendarEventResult("evt-lost-response", "https://meet.google.com/xxx-yyyy-zzz", false));
        _calendar.OnUpdate = (id, _) => Result.Success(new CalendarEventResult(id, null, false));

        await RunAsync();

        Assert.Contains(_calendar.Calls, c => c.Operation == "find");
        Assert.DoesNotContain(_calendar.Calls, c => c.Operation == "create");
        Assert.Equal("evt-lost-response", meeting.PROVIDER_EVENT_ID);
        Assert.Equal("https://meet.google.com/xxx-yyyy-zzz", _protector.Decrypt(meeting.MEET_URL_ENCRYPTED!));
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
    }

    [Fact]
    public async Task Pending_AfterAFailedAttempt_NothingFoundOnGoogle_CreatesTheEvent()
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession(m => m.RecordAttemptFailed(MeetingErrorCodes.GoogleTransient, _clock));
        _clock.UtcNow = LiveTestData.Now.AddMinutes(2);

        await RunAsync();

        Assert.Equal(["find", "create"], _calendar.Calls.Select(c => c.Operation).ToArray());
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
    }

    [Fact]
    public async Task Pending_FirstAttempt_DoesNotBotherSearchingForAnExistingEvent()
    {
        ConnectedAccount();
        PendingSession();

        await RunAsync();

        Assert.DoesNotContain(_calendar.Calls, c => c.Operation == "find");
    }

    // ---- Conference pending ---------------------------------------------------------------------------

    [Fact]
    public async Task Pending_ConferenceStillBeingCreated_PollsThreeTimes_ThenRecordsAFailedAttempt()
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession();
        _calendar.OnCreate = _ => Result.Success(new CalendarEventResult("evt-pending", null, ConferencePending: true));
        _calendar.OnGet = _ => Result.Success(new CalendarEventResult("evt-pending", null, ConferencePending: true));

        await RunAsync();

        Assert.Equal(3, _calendar.Calls.Count(c => c.Operation == "get"));
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ATTEMPTS);
        Assert.Equal(MeetingErrorCodes.ConferencePending, meeting.ERROR);
        Assert.Equal(LiveTestData.Now.AddMinutes(1), meeting.NEXT_RETRY_AT_UTC);
        Assert.Null(meeting.MEET_URL_ENCRYPTED);
    }

    [Fact]
    public async Task Pending_ConferenceReadyOnTheSecondPoll_IsSynced()
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession();
        var polls = 0;
        _calendar.OnCreate = _ => Result.Success(new CalendarEventResult("evt-slow", null, ConferencePending: true));
        _calendar.OnGet = _ => ++polls < 2
            ? Result.Success(new CalendarEventResult("evt-slow", null, ConferencePending: true))
            : Result.Success(new CalendarEventResult("evt-slow", GoodMeetUrl, ConferencePending: false));

        await RunAsync();

        Assert.Equal(2, polls);
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.Equal("evt-slow", meeting.PROVIDER_EVENT_ID);
    }

    [Fact]
    public async Task Pending_CreateYieldsNoRoomAndNoPendingConference_IsAFailedAttemptWithConferenceFailed()
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession();
        _calendar.OnCreate = _ => Result.Success(new CalendarEventResult("evt-no-meet", null, ConferencePending: false));

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ATTEMPTS);
        Assert.Equal(MeetingErrorCodes.ConferenceFailed, meeting.ERROR);
        Assert.DoesNotContain(_calendar.Calls, c => c.Operation == "get");
    }

    // ---- URL allow-list --------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://evil.example.test/room")]
    [InlineData("http://meet.google.com/abc")]
    [InlineData("javascript:alert(1)")]
    public async Task Pending_GoogleReturnsAUrlOffTheAllowList_IsNeverStored(string url)
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession();
        _calendar.OnCreate = _ => Result.Success(new CalendarEventResult("evt-x", url, false));

        await RunAsync();

        Assert.Null(meeting.MEET_URL_ENCRYPTED);
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Equal(MeetingErrorCodes.MeetUrlRejected, meeting.ERROR);
    }

    // ---- Logging provider (development) ------------------------------------------------------------------

    [Fact]
    public async Task Pending_LoggingProvider_NeedsNoAccount_AndAcceptsTheFakeHost()
    {
        _mode = LiveProviderMode.Logging;
        var (_, meeting) = PendingSession();
        _calendar.OnCreate = request => Result.Success(new CalendarEventResult($"dev-{request.RequestId}", $"{LoggingCalendarProvider.DevMeetUrlPrefix}{request.RequestId}", false));

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.Equal(MeetingProvider.Logging, meeting.PROVIDER);
        Assert.Null(meeting.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
        Assert.StartsWith(LoggingCalendarProvider.DevMeetUrlPrefix, _protector.Decrypt(meeting.MEET_URL_ENCRYPTED!));
        Assert.Equal(0, _oauth.RefreshCalls);
    }

    [Fact]
    public async Task Pending_LoggingProvider_StillRejectsAnythingThatIsNotTheFakeUrl()
    {
        _mode = LiveProviderMode.Logging;
        var (_, meeting) = PendingSession();
        _calendar.OnCreate = _ => Result.Success(new CalendarEventResult("dev-x", "https://evil.example.test/room", false));

        await RunAsync();

        Assert.Null(meeting.MEET_URL_ENCRYPTED);
        Assert.Equal(MeetingErrorCodes.MeetUrlRejected, meeting.ERROR);
    }

    // ---- Failures and backoff --------------------------------------------------------------------------

    [Fact]
    public async Task Pending_TransientGoogleFailures_BackOff_1_5_15_60_Minutes_ThenFailed_AndTheInstructorIsToldOnce()
    {
        ConnectedAccount();
        var (context, meeting) = PendingSession();
        _calendar.OnCreate = _ => Result.Failure<CalendarEventResult>(GoogleErrors.Transient("down"));
        _calendar.OnFind = _ => Result.Success<CalendarEventResult?>(null);

        var expectedDelays = new[] { 1, 5, 15, 60 };
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await RunAsync();

            Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
            Assert.Equal(attempt, meeting.ATTEMPTS);
            Assert.Equal(_clock.UtcNow.AddMinutes(expectedDelays[attempt - 1]), meeting.NEXT_RETRY_AT_UTC);
            Assert.Equal(MeetingErrorCodes.GoogleTransient, meeting.ERROR);

            // Not yet due: the job leaves it alone.
            var before = _calendar.Calls.Count;
            await RunAsync();
            Assert.Equal(before, _calendar.Calls.Count);

            _clock.UtcNow = meeting.NEXT_RETRY_AT_UTC!.Value;
        }

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Failed, meeting.SYNC_STATUS);
        Assert.Equal(5, meeting.ATTEMPTS);
        Assert.Null(meeting.NEXT_RETRY_AT_UTC);
        Assert.Equal([(InstructorId, context.SessionId)], _alerts.FailedAlerts);

        await RunAsync(); // Failed is not picked up again
        Assert.Single(_alerts.FailedAlerts);
    }

    [Theory]
    [InlineData(GoogleErrors.RateLimitedCode, MeetingErrorCodes.GoogleRateLimited)]
    [InlineData(GoogleErrors.BadRequestCode, MeetingErrorCodes.GoogleBadRequest)]
    [InlineData(GoogleErrors.TransientCode, MeetingErrorCodes.GoogleTransient)]
    public async Task Pending_CalendarErrors_AreStoredAsShortCodes(string googleCode, string expectedStoredCode)
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession();
        _calendar.OnCreate = _ => Result.Failure<CalendarEventResult>(new DomainError(googleCode, "details that must not be stored"));

        await RunAsync();

        Assert.Equal(expectedStoredCode, meeting.ERROR);
        Assert.DoesNotContain("details", meeting.ERROR);
    }

    [Fact]
    public async Task Pending_RefreshTokenRejected_NeedsReconnect_TheAccountIsRevoked_AndTheInstructorAlertedOnce()
    {
        var account = ConnectedAccount();
        var (_, first) = PendingSession();
        var (_, second) = PendingSession();
        _oauth.RefreshResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Unauthorized("dead", "invalid_grant"));

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.NeedsReconnect, first.SYNC_STATUS);
        Assert.Equal(MeetingSyncStatus.NeedsReconnect, second.SYNC_STATUS);
        Assert.Equal(MeetingErrorCodes.InvalidGrant, first.ERROR);
        Assert.Equal(MeetingErrorCodes.InvalidGrant, second.ERROR);
        Assert.False(account.IsActive);
        Assert.Single(_alerts.ReconnectAlerts);
        Assert.Empty(_alerts.NeedsLinkAlerts); // told to reconnect once; not also asked to paste a link per class
        Assert.Equal(1, _oauth.RefreshCalls); // one Google call for the whole run, not one per meeting
        Assert.Empty(_calendar.Calls);
    }

    [Fact]
    public async Task Pending_AccountDisconnectedOnPurpose_IsAskedToPasteALink_NotToReconnect()
    {
        var account = ConnectedAccount();
        account.MarkRevoked(GoogleAccountRevokedReason.UserDisconnected, _clock);
        var (_, meeting) = PendingSession();

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Single(_alerts.NeedsLinkAlerts);
        Assert.Empty(_alerts.ReconnectAlerts);
    }

    [Fact]
    public async Task Pending_AccountRevokedEarlier_WaitsForTheReconnect_WithoutANewAlert()
    {
        var account = ConnectedAccount();
        account.MarkRevoked(GoogleAccountRevokedReason.InsufficientScope, _clock);
        var (_, meeting) = PendingSession();

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.NeedsReconnect, meeting.SYNC_STATUS);
        Assert.Equal("insufficient_scope", meeting.ERROR);
        Assert.Empty(_alerts.NeedsLinkAlerts);
        Assert.Empty(_alerts.ReconnectAlerts);
    }

    [Fact]
    public async Task Pending_ClientCredentialsRejected_IsRetriedNotRevoked_AndNobodyIsToldToReconnect()
    {
        var account = ConnectedAccount();
        var (_, meeting) = PendingSession();
        _oauth.RefreshResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Unauthorized("bad client", "invalid_client"));

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ATTEMPTS);
        Assert.Equal(MeetingErrorCodes.GoogleClientMisconfigured, meeting.ERROR);
        Assert.True(account.IsActive);
        Assert.Empty(_alerts.ReconnectAlerts);
        Assert.Empty(_calendar.Calls);
    }

    /// <summary>D1: a Workers host with the wrong encryption key cannot read any instructor's refresh token. That must never turn into mass
    /// revocations + "reconnect Google" e-mails — it is recorded as a platform-configuration failure and retried.</summary>
    [Fact]
    public async Task Pending_StoredRefreshTokenCannotBeDecrypted_IsRetriedNotRevoked_AndNobodyIsToldToReconnect()
    {
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            InstructorId, "sub", "teacher@gmail.test", LiveTestData.Protector().Encrypt("encrypted-with-the-real-key"), GoogleScopes.CalendarEventsOwned, _clock);
        _accounts.Accounts.Add(account);
        var (_, first) = PendingSession();
        var (_, second) = PendingSession();

        await RunAsync();

        Assert.All([first, second], meeting =>
        {
            Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
            Assert.Equal(1, meeting.ATTEMPTS);
            Assert.Equal(MeetingErrorCodes.GoogleClientMisconfigured, meeting.ERROR);
        });
        Assert.True(account.IsActive);
        Assert.NotNull(account.REFRESH_TOKEN_ENCRYPTED);
        Assert.Null(account.REVOKED_REASON);
        Assert.Empty(_alerts.ReconnectAlerts);
        Assert.Empty(_alerts.NeedsLinkAlerts);
        Assert.Equal(0, _oauth.RefreshCalls);
        Assert.Empty(_calendar.Calls);
        Assert.Contains("DataProtection:EncryptionKeyBase64", _serviceLog.All);
    }

    [Fact]
    public async Task Pending_StoredRefreshTokenCannotBeDecrypted_ThenTheKeyIsFixed_TheMeetingSyncsOnTheNextAttempt()
    {
        var realProtector = _protector;
        var wrongKeyProtector = LiveTestData.Protector();
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            InstructorId, "sub", "teacher@gmail.test", realProtector.Encrypt("refresh-token-1"), GoogleScopes.CalendarEventsOwned, _clock);
        _accounts.Accounts.Add(account);
        var (_, meeting) = PendingSession();

        // A Workers host that runs with a different key: the credential is unreadable, the attempt is recorded, nothing is revoked.
        await new LiveMeetingSyncJob(
            _meetings, _schedule, GoogleAccounts(wrongKeyProtector), _calendar, new MeetingLinkValidator(LiveTestData.OptionsOf()), _alerts, wrongKeyProtector, _clock,
            LiveTestData.OptionsOf(), _jobLog) { ConferencePollInterval = TimeSpan.Zero }.RunAsync(CancellationToken.None);
        Assert.Equal(1, meeting.ATTEMPTS);
        Assert.True(account.IsActive);

        // The operator fixes the key; once the backoff has elapsed the very same credential works.
        _clock.UtcNow = LiveTestData.Now.AddMinutes(2);
        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.True(account.IsActive);
        Assert.Empty(_alerts.ReconnectAlerts);
    }

    private InstructorGoogleAccountService GoogleAccounts(ISensitiveDataProtector protector) => new(
        _accounts,
        _meetings,
        _oauth,
        new FakeStateStore(),
        protector,
        _schedule,
        _alerts,
        _clock,
        LiveTestData.OptionsOf(),
        Microsoft.Extensions.Options.Options.Create(new GoogleOAuthOptions()),
        _serviceLog);

    [Fact]
    public async Task Pending_CalendarSaysUnauthorized_RevokesTheAccount_AndNeedsReconnect()
    {
        var account = ConnectedAccount();
        var (_, meeting) = PendingSession();
        _calendar.OnCreate = _ => Result.Failure<CalendarEventResult>(GoogleErrors.Unauthorized("401"));

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.NeedsReconnect, meeting.SYNC_STATUS);
        Assert.Equal(MeetingErrorCodes.InvalidGrant, meeting.ERROR);
        Assert.False(account.IsActive);
        Assert.Equal("invalid_grant", account.REVOKED_REASON);
        Assert.Single(_alerts.ReconnectAlerts);
    }

    [Fact]
    public async Task Pending_CalendarSaysInsufficientScope_RevokesWithThatReason()
    {
        var account = ConnectedAccount();
        var (_, meeting) = PendingSession();
        _calendar.OnCreate = _ => Result.Failure<CalendarEventResult>(GoogleErrors.Unauthorized("403", "insufficientPermissions"));

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.NeedsReconnect, meeting.SYNC_STATUS);
        Assert.Equal(MeetingErrorCodes.InsufficientScope, meeting.ERROR);
        Assert.Equal("insufficient_scope", account.REVOKED_REASON);
    }

    // ---- Finished / missing / cancelled sessions ----------------------------------------------------------

    [Fact]
    public async Task Pending_SessionAlreadyFinished_NoRoomIsBuilt_AndThereIsNothingToKeep()
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession(context: LiveTestData.Context(instructorUserId: InstructorId, startsAtUtc: LiveTestData.Now.AddHours(-3), endsAtUtc: LiveTestData.Now.AddHours(-1)));

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
        Assert.Empty(_calendar.Calls);
    }

    [Fact]
    public async Task Pending_SessionAlreadyFinished_WithAnExistingUrl_StaysSyncedAndUsable()
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession(
            m =>
            {
                m.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
                m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt", _protector.Encrypt(GoodMeetUrl), Guid.NewGuid(), _clock);
                m.MarkSessionChanged();
            },
            LiveTestData.Context(instructorUserId: InstructorId, startsAtUtc: LiveTestData.Now.AddHours(-3), endsAtUtc: LiveTestData.Now.AddHours(-1)));

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.True(meeting.IsUsable);
        Assert.Empty(_calendar.Calls);
    }

    [Fact]
    public async Task Pending_TheSessionNoLongerExists_IsDeleted()
    {
        ConnectedAccount();
        var meeting = SESSION_MEETING.Stage(Guid.NewGuid()); // no matching context
        _meetings.Meetings.Add(meeting);

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
        Assert.Empty(_calendar.Calls);
    }

    [Fact]
    public async Task Pending_CancelledSessionWithoutAGoogleEvent_IsDeleted_WithoutAnyGoogleCall()
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession(context: LiveTestData.Context(instructorUserId: InstructorId, status: LiveSessionStatus.Cancelled));

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
        Assert.Empty(_calendar.Calls);
    }

    [Fact]
    public async Task Pending_CancelledSessionWithAGoogleEvent_DeletesTheEvent_ThenIsDeleted()
    {
        var account = ConnectedAccount();
        var (_, meeting) = PendingSession(
            m =>
            {
                m.AssignProvider(MeetingProvider.GoogleMeet, account.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
                m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-to-delete", _protector.Encrypt(GoodMeetUrl), account.INSTRUCTOR_GOOGLE_ACCOUNT_ID, _clock);
                m.MarkSessionChanged();
            },
            LiveTestData.Context(instructorUserId: InstructorId, status: LiveSessionStatus.Cancelled));

        await RunAsync();

        Assert.Single(_calendar.Calls, c => c.Operation == "delete" && c.EventId == "evt-to-delete");
        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
        Assert.Null(meeting.PROVIDER_EVENT_ID);
    }

    // ---- PendingDelete ---------------------------------------------------------------------------------

    [Fact]
    public async Task PendingDelete_ManualLinkReplacedAnEvent_DeletesItAndSettlesSyncedWithTheManualUrl()
    {
        var account = ConnectedAccount();
        var (_, meeting) = PendingSession(m =>
        {
            m.AssignProvider(MeetingProvider.GoogleMeet, account.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
            m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-replaced", _protector.Encrypt(GoodMeetUrl), account.INSTRUCTOR_GOOGLE_ACCOUNT_ID, _clock);
            m.SetManualLink(_protector.Encrypt("https://zoom.us/j/55"));
        });
        Assert.Equal(MeetingSyncStatus.PendingDelete, meeting.SYNC_STATUS);

        await RunAsync();

        Assert.Single(_calendar.Calls, c => c.Operation == "delete" && c.EventId == "evt-replaced");
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.Null(meeting.PROVIDER_EVENT_ID);
        Assert.Equal("https://zoom.us/j/55", _protector.Decrypt(meeting.MEET_URL_ENCRYPTED!));
    }

    [Fact]
    public async Task PendingDelete_NoUsableAccount_TheEventIsLeftAsAnOrphan_ButTheMeetingStillSettles()
    {
        var (_, meeting) = PendingSession(m =>
        {
            m.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
            m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-orphan", _protector.Encrypt(GoodMeetUrl), Guid.NewGuid(), _clock);
            m.SetManualLink(_protector.Encrypt("https://zoom.us/j/55"));
        });

        await RunAsync();

        Assert.Empty(_calendar.Calls);
        Assert.Equal(MeetingErrorCodes.OrphanEvent, meeting.ERROR);
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.Null(meeting.PROVIDER_EVENT_ID);
    }

    [Fact]
    public async Task PendingDelete_RefreshTokenDead_OrphansTheEvent_AndDoesNotRetryForever()
    {
        ConnectedAccount();
        _oauth.RefreshResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Unauthorized("dead", "invalid_grant"));
        var (_, meeting) = PendingSession(m =>
        {
            m.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
            m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-orphan", _protector.Encrypt(GoodMeetUrl), Guid.NewGuid(), _clock);
            m.MarkSessionCancelled();
        });

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
        Assert.Equal(MeetingErrorCodes.OrphanEvent, meeting.ERROR);
    }

    [Fact]
    public async Task PendingDelete_TransientDeleteFailure_KeepsTheEventAndRetriesLater()
    {
        var account = ConnectedAccount();
        _calendar.OnDelete = _ => Result.Failure(GoogleErrors.Transient("down"));
        var (_, meeting) = PendingSession(m =>
        {
            m.AssignProvider(MeetingProvider.GoogleMeet, account.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
            m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-stay", _protector.Encrypt(GoodMeetUrl), account.INSTRUCTOR_GOOGLE_ACCOUNT_ID, _clock);
            m.MarkSessionCancelled();
        });

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.PendingDelete, meeting.SYNC_STATUS);
        Assert.Equal("evt-stay", meeting.PROVIDER_EVENT_ID);
        Assert.Equal(1, meeting.ATTEMPTS);
        Assert.Equal(LiveTestData.Now.AddMinutes(1), meeting.NEXT_RETRY_AT_UTC);
    }

    [Fact]
    public async Task PendingDelete_ProviderTreatsGone404AsSuccess_SoTheMeetingSettles()
    {
        var account = ConnectedAccount();
        _calendar.OnDelete = _ => Result.Success(); // GoogleCalendarProvider maps 404/410 to Success
        var (_, meeting) = PendingSession(m =>
        {
            m.AssignProvider(MeetingProvider.GoogleMeet, account.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
            m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-gone", _protector.Encrypt(GoodMeetUrl), account.INSTRUCTOR_GOOGLE_ACCOUNT_ID, _clock);
            m.MarkSessionCancelled();
        });

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
    }

    // ---- Run-level behaviour -----------------------------------------------------------------------------

    [Fact]
    public async Task Run_AdoptsUpcomingSessionsThatHaveNoMeetingRow_AndProcessesThemTheSameRun()
    {
        ConnectedAccount();
        var orphan = LiveTestData.Context(instructorUserId: InstructorId);
        _schedule.Contexts.Add(orphan);

        await RunAsync();

        var adopted = Assert.Single(_meetings.Meetings);
        Assert.Equal(orphan.SessionId, adopted.SESSION_ID);
        Assert.Equal(MeetingSyncStatus.Synced, adopted.SYNC_STATUS);
    }

    [Fact]
    public async Task Run_AdoptionIsCappedAtTwoHundredRowsPerRun()
    {
        for (var i = 0; i < 250; i++)
        {
            _schedule.Contexts.Add(LiveTestData.Context(instructorUserId: InstructorId, startsAtUtc: LiveTestData.Now.AddDays(1).AddMinutes(i)));
        }

        await RunAsync();

        Assert.Equal(LiveMeetingSyncJob.OrphanBatchLimit, _meetings.Meetings.Count);
    }

    [Fact]
    public async Task Run_CancelledSessionsWithoutARowAreNotAdopted()
    {
        _schedule.Contexts.Add(LiveTestData.Context(instructorUserId: InstructorId, status: LiveSessionStatus.Cancelled));

        await RunAsync();

        Assert.Empty(_meetings.Meetings);
    }

    [Fact]
    public async Task Run_OnlyProcessesTheBatchSize_RestWaitForTheNextRun()
    {
        ConnectedAccount();
        for (var i = 0; i < LiveMeetingSyncJob.BatchSize + 5; i++)
        {
            PendingSession();
        }

        await RunAsync();

        Assert.Equal(LiveMeetingSyncJob.BatchSize, _meetings.Meetings.Count(m => m.SYNC_STATUS == MeetingSyncStatus.Synced));
        Assert.Equal(5, _meetings.Meetings.Count(m => m.SYNC_STATUS == MeetingSyncStatus.Pending));
    }

    [Fact]
    public async Task Run_OneMeetingThrowing_DoesNotStopTheOthers_AndThePoisonedRowRecordsAnAttempt()
    {
        ConnectedAccount();
        var (_, poisoned) = PendingSession();
        var (_, healthy) = PendingSession();
        _calendar.OnCreate = request => request.PrivateSessionId == _schedule.Contexts[0].SessionId.ToString("N")
            ? throw new InvalidOperationException("kaboom")
            : Result.Success(new CalendarEventResult("evt-ok", GoodMeetUrl, false));

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Synced, healthy.SYNC_STATUS);
        Assert.Equal(MeetingSyncStatus.Pending, poisoned.SYNC_STATUS);
        Assert.Equal(1, poisoned.ATTEMPTS);
        Assert.Equal("internal_error", poisoned.ERROR);
        Assert.True(_meetings.ClearTrackingCount >= 1);
    }

    [Fact]
    public async Task Run_ConcurrencyConflictOnSave_IsSkipped_NotRecordedAsAFailure()
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession();
        _meetings.ThrowOnNextSave = new DbUpdateConcurrencyException("someone else changed it");

        await RunAsync();

        Assert.Equal(1, _meetings.ClearTrackingCount);
        Assert.Equal(0, _meetings.SaveCount);
        Assert.Equal(0, meeting.ATTEMPTS); // a conflict is not a failed attempt
        Assert.DoesNotContain("internal_error", meeting.ERROR ?? string.Empty);
    }

    [Fact]
    public async Task Run_DueSelection_SkipsMeetingsWhoseRetryTimeHasNotArrived()
    {
        ConnectedAccount();
        var (_, waiting) = PendingSession(m => m.RecordAttemptFailed(MeetingErrorCodes.GoogleTransient, _clock)); // retry at +1 min
        var (_, ready) = PendingSession();

        await RunAsync();

        Assert.Equal(MeetingSyncStatus.Pending, waiting.SYNC_STATUS);
        Assert.Equal(MeetingSyncStatus.Synced, ready.SYNC_STATUS);
    }

    [Fact]
    public async Task Run_CanceledToken_StopsWithoutTouchingTheMeeting()
    {
        ConnectedAccount();
        var (_, meeting) = PendingSession();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Job().RunAsync(cts.Token));

        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
    }

    // ---- Secrets never reach a log ------------------------------------------------------------------------

    [Fact]
    public async Task Logs_NeverContainTokensRoomUrlsOrEmailAddresses()
    {
        ConnectedAccount("TOP-SECRET-REFRESH");
        PendingSession();
        PendingSession();
        _calendar.OnCreate = _ => Result.Failure<CalendarEventResult>(GoogleErrors.Transient("down"));
        for (var i = 0; i < 5; i++)
        {
            await RunAsync();
            _clock.UtcNow = _clock.UtcNow.AddHours(2);
        }

        var logs = _jobLog.All + "\n" + _serviceLog.All;
        Assert.DoesNotContain("TOP-SECRET-REFRESH", logs);
        Assert.DoesNotContain("access-token-2", logs);
        Assert.DoesNotContain("meet.google.com", logs);
        Assert.DoesNotContain("teacher@gmail.test", logs);
    }

    [Fact]
    public async Task Logs_AfterASuccessfulSync_NeverContainTheMeetUrl()
    {
        ConnectedAccount();
        PendingSession();

        await RunAsync();

        Assert.DoesNotContain("meet.google.com", _jobLog.All + _serviceLog.All);
        Assert.DoesNotContain("abc-defg-hij", _jobLog.All + _serviceLog.All);
    }

    // ---- Fake calendar -----------------------------------------------------------------------------------

    private sealed record Call(string Operation, string AccessToken, string? EventId, CalendarEventRequest? Request);

    private sealed class FakeCalendar : ICalendarProvider
    {
        public List<Call> Calls { get; } = [];

        public Func<CalendarEventRequest, Result<CalendarEventResult>> OnCreate { get; set; } =
            _ => Result.Success(new CalendarEventResult("evt-created", GoodMeetUrl, ConferencePending: false));

        public Func<string, CalendarEventRequest, Result<CalendarEventResult>> OnUpdate { get; set; } =
            (eventId, _) => Result.Success(new CalendarEventResult(eventId, GoodMeetUrl, ConferencePending: false));

        public Func<string, Result<CalendarEventResult?>> OnFind { get; set; } = _ => Result.Success<CalendarEventResult?>(null);

        public Func<string, Result<CalendarEventResult>> OnGet { get; set; } =
            eventId => Result.Success(new CalendarEventResult(eventId, GoodMeetUrl, ConferencePending: false));

        public Func<string, Result> OnDelete { get; set; } = _ => Result.Success();

        public Task<Result<CalendarEventResult>> CreateEventWithMeetAsync(string accessToken, CalendarEventRequest request, CancellationToken ct)
        {
            Calls.Add(new Call("create", accessToken, null, request));
            return Task.FromResult(OnCreate(request));
        }

        public Task<Result<CalendarEventResult?>> FindEventByPrivateSessionIdAsync(string accessToken, string privateSessionId, CancellationToken ct)
        {
            Calls.Add(new Call("find", accessToken, null, null));
            return Task.FromResult(OnFind(privateSessionId));
        }

        public Task<Result<CalendarEventResult>> GetEventAsync(string accessToken, string eventId, CancellationToken ct)
        {
            Calls.Add(new Call("get", accessToken, eventId, null));
            return Task.FromResult(OnGet(eventId));
        }

        public Task<Result<CalendarEventResult>> UpdateEventAsync(string accessToken, string eventId, CalendarEventRequest request, CancellationToken ct)
        {
            Calls.Add(new Call("update", accessToken, eventId, request));
            return Task.FromResult(OnUpdate(eventId, request));
        }

        public Task<Result> DeleteEventAsync(string accessToken, string eventId, CancellationToken ct)
        {
            Calls.Add(new Call("delete", accessToken, eventId, null));
            return Task.FromResult(OnDelete(eventId));
        }

        public Task<Result> SetAttendeesAsync(string accessToken, string eventId, IReadOnlyCollection<string> attendeeEmails, CancellationToken ct) =>
            throw new NotSupportedException("Attendee sync belongs to P11-04.");
    }
}
