using Siri.Integrations.Google;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

internal sealed class FakeUserContext(Guid? userId) : IUserContext
{
    public Guid? UserId { get; } = userId;

    public IReadOnlyCollection<string> Roles => [];

    public bool IsAuthenticated => UserId is not null;
}

/// <summary>
/// The provider decision that no longer waits for the background job: when a class is created or edited, the sink settles — in the same unit of work, with no
/// network call — whether a room can be built automatically at all. "No" (Live:Provider=ManualOnly, or the instructor has no usable Google account) is committed
/// at once as Manual/AwaitingLink (or NeedsReconnect for a broken connection), so the screen says "paste the link" immediately and the publish gate works with no
/// worker running; "yes" leaves the row Pending for the job, which stays the reconciler.
/// </summary>
public class LiveMeetingSinkProviderDecisionTests
{
    private static readonly Guid InstructorId = Guid.NewGuid();

    private readonly InMemorySessionMeetingRepository _meetings = new();
    private readonly InMemoryAccountRepository _accounts = new();
    private readonly FakeClock _clock = new(LiveTestData.Now);

    private LiveMeetingSink Sink(LiveProviderMode mode = LiveProviderMode.GoogleMeet, bool signedIn = true) =>
        new(
            _meetings,
            _accounts,
            new FakeUserContext(signedIn ? InstructorId : null),
            LiveTestData.OptionsOf(o => o.Provider = mode));

    private INSTRUCTOR_GOOGLE_ACCOUNT ActiveAccount()
    {
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(InstructorId, "sub", "teacher@gmail.test", "enc-refresh", GoogleScopes.CalendarEventsOwned, _clock);
        _accounts.Accounts.Add(account);
        return account;
    }

    // ---- Decided at once: no automatic room is possible ----------------------------------------------------

    [Fact]
    public async Task Scheduled_ManualOnly_IsAwaitingLinkImmediately_WithTheInstructorRecorded_AndNoSave()
    {
        var sessionId = Guid.NewGuid();

        await Sink(LiveProviderMode.ManualOnly).OnSessionScheduledAsync(sessionId, CancellationToken.None);

        var meeting = Assert.Single(_meetings.Meetings);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Equal(MeetingProvider.Manual, meeting.PROVIDER);
        Assert.Equal(InstructorId, meeting.INSTRUCTOR_USER_ID); // how a later Google connection finds this row again
        Assert.Null(meeting.MEET_URL_ENCRYPTED);
        Assert.False(meeting.IsUsable);
        Assert.Equal(0, _meetings.SaveCount); // the Catalog handler still owns the transaction
    }

    [Fact]
    public async Task Scheduled_GoogleMeetMode_WhenTheInstructorNeverConnectedGoogle_IsAwaitingLinkImmediately()
    {
        await Sink().OnSessionScheduledAsync(Guid.NewGuid(), CancellationToken.None);

        var meeting = Assert.Single(_meetings.Meetings);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Equal(MeetingProvider.Manual, meeting.PROVIDER);
        Assert.Equal(InstructorId, meeting.INSTRUCTOR_USER_ID);
    }

    [Fact]
    public async Task Scheduled_WhenTheInstructorDisconnectedGoogleOnPurpose_IsAwaitingLink()
    {
        ActiveAccount().MarkRevoked(GoogleAccountRevokedReason.UserDisconnected, _clock);

        await Sink().OnSessionScheduledAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(MeetingSyncStatus.AwaitingLink, Assert.Single(_meetings.Meetings).SYNC_STATUS);
    }

    [Theory]
    [InlineData(GoogleAccountRevokedReason.InvalidGrant)]
    [InlineData(GoogleAccountRevokedReason.ScopeMissing)]
    [InlineData(GoogleAccountRevokedReason.InsufficientScope)]
    public async Task Scheduled_WhenTheGoogleConnectionBroke_IsNeedsReconnect_NotAPasteLinkPrompt(string reason)
    {
        // The instructor was already told, once, to reconnect; their classes wait for that rather than asking for a link each time (same rule as the job).
        ActiveAccount().MarkRevoked(reason, _clock);

        await Sink().OnSessionScheduledAsync(Guid.NewGuid(), CancellationToken.None);

        var meeting = Assert.Single(_meetings.Meetings);
        Assert.Equal(MeetingSyncStatus.NeedsReconnect, meeting.SYNC_STATUS);
        Assert.Equal(reason, meeting.ERROR);
        Assert.Equal(InstructorId, meeting.INSTRUCTOR_USER_ID);
    }

    // ---- Left to the job: a call to Google is needed --------------------------------------------------------

    [Fact]
    public async Task Scheduled_WithAnActiveGoogleAccount_StaysPending_ForTheJobToCallGoogle()
    {
        ActiveAccount();

        await Sink().OnSessionScheduledAsync(Guid.NewGuid(), CancellationToken.None);

        var meeting = Assert.Single(_meetings.Meetings);
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Null(meeting.PROVIDER); // the job assigns GoogleMeet before the first call, as before
        Assert.Equal(InstructorId, meeting.INSTRUCTOR_USER_ID);
    }

    [Fact]
    public async Task Scheduled_LoggingMode_StaysPending_TheJobBuildsTheFakeRoom()
    {
        await Sink(LiveProviderMode.Logging).OnSessionScheduledAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(MeetingSyncStatus.Pending, Assert.Single(_meetings.Meetings).SYNC_STATUS);
    }

    [Fact]
    public async Task Scheduled_WithNoSignedInUser_StaysPending_TheJobDecides()
    {
        await Sink(signedIn: false).OnSessionScheduledAsync(Guid.NewGuid(), CancellationToken.None);

        var meeting = Assert.Single(_meetings.Meetings);
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Null(meeting.INSTRUCTOR_USER_ID);
    }

    // ---- Edits ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Changed_ForAnUndecidedPendingRow_DecidesNow()
    {
        var sessionId = Guid.NewGuid();
        _meetings.Meetings.Add(SESSION_MEETING.Stage(sessionId)); // staged earlier (e.g. while the user was unknown), still undecided

        await Sink().OnSessionChangedAsync(sessionId, CancellationToken.None);

        var meeting = Assert.Single(_meetings.Meetings);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ICS_SEQUENCE); // the edit still counts
    }

    [Fact]
    public async Task Changed_ForAClassWithNoMeetingRow_StagesAndDecides()
    {
        var sessionId = Guid.NewGuid();

        await Sink().OnSessionChangedAsync(sessionId, CancellationToken.None);

        var meeting = Assert.Single(_meetings.Meetings);
        Assert.Equal(sessionId, meeting.SESSION_ID);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
    }

    [Fact]
    public async Task Changed_ForARowAlreadyAwaitingALink_KeepsItsDecision_AndOnlyBumpsTheSequence()
    {
        var sessionId = Guid.NewGuid();
        var meeting = SESSION_MEETING.Stage(sessionId);
        meeting.AssignInstructor(InstructorId);
        meeting.ResolveAsAwaitingLink();
        _meetings.Meetings.Add(meeting);
        ActiveAccount(); // even if Google was connected since, an edit does not silently flip the row: reconnecting resets it

        await Sink().OnSessionChangedAsync(sessionId, CancellationToken.None);

        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Equal(MeetingProvider.Manual, meeting.PROVIDER);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
    }

    [Fact]
    public async Task Changed_ForAGoogleBackedRoom_GoesBackToPending_ForTheJobToPatchTheEvent()
    {
        var sessionId = Guid.NewGuid();
        var meeting = SESSION_MEETING.Stage(sessionId);
        meeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
        meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt", "enc", Guid.NewGuid(), _clock);
        _meetings.Meetings.Add(meeting);

        await Sink(LiveProviderMode.ManualOnly).OnSessionChangedAsync(sessionId, CancellationToken.None);

        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS); // never turned into a paste-a-link prompt behind the event's back
        Assert.Equal(MeetingProvider.GoogleMeet, meeting.PROVIDER);
    }

    [Fact]
    public async Task Changed_ForARoomThatAlreadyHasAManualLink_IsLeftUsable()
    {
        var sessionId = Guid.NewGuid();
        var meeting = SESSION_MEETING.Stage(sessionId);
        meeting.SetManualLink("enc-url");
        _meetings.Meetings.Add(meeting);

        await Sink().OnSessionChangedAsync(sessionId, CancellationToken.None);

        Assert.True(meeting.IsUsable);
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
    }

    [Fact]
    public async Task Scheduled_TwiceForTheSameSession_StillOneRow_AndOneDecision()
    {
        var sessionId = Guid.NewGuid();
        var sink = Sink();

        await sink.OnSessionScheduledAsync(sessionId, CancellationToken.None);
        await sink.OnSessionScheduledAsync(sessionId, CancellationToken.None);

        var meeting = Assert.Single(_meetings.Meetings);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Equal(0, meeting.ICS_SEQUENCE);
    }
}

/// <summary>The one rule behind the provider choice, shared by the sink (at creation) and the sync job (for anything still pending).</summary>
public class MeetingProviderDecisionTests
{
    private static readonly FakeClock Clock = new(LiveTestData.Now);

    private static INSTRUCTOR_GOOGLE_ACCOUNT Account() =>
        INSTRUCTOR_GOOGLE_ACCOUNT.Connect(Guid.NewGuid(), "sub", "teacher@gmail.test", "enc", GoogleScopes.CalendarEventsOwned, Clock);

    [Fact]
    public void Logging_AlwaysNeedsTheExternalCall() =>
        Assert.Equal(MeetingProviderOutcome.NeedsExternalCall, MeetingProviderDecision.ForNewRoom(LiveProviderMode.Logging, null).Outcome);

    [Fact]
    public void ManualOnly_IsAwaitingLink_EvenWithAnActiveAccount() =>
        Assert.Equal(MeetingProviderOutcome.AwaitingLink, MeetingProviderDecision.ForNewRoom(LiveProviderMode.ManualOnly, Account()).Outcome);

    [Fact]
    public void GoogleMeet_WithAnActiveAccount_NeedsTheExternalCall() =>
        Assert.Equal(MeetingProviderOutcome.NeedsExternalCall, MeetingProviderDecision.ForNewRoom(LiveProviderMode.GoogleMeet, Account()).Outcome);

    [Fact]
    public void GoogleMeet_WithNoAccount_IsAwaitingLink() =>
        Assert.Equal(MeetingProviderOutcome.AwaitingLink, MeetingProviderDecision.ForNewRoom(LiveProviderMode.GoogleMeet, null).Outcome);

    [Fact]
    public void GoogleMeet_WithABrokenConnection_IsNeedsReconnect_WithTheStoredReason()
    {
        var broken = Account();
        broken.MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, Clock);

        var decision = MeetingProviderDecision.ForNewRoom(LiveProviderMode.GoogleMeet, broken);

        Assert.Equal(MeetingProviderOutcome.NeedsReconnect, decision.Outcome);
        Assert.Equal(GoogleAccountRevokedReason.InvalidGrant, decision.ReconnectReason);
    }

    [Fact]
    public void BrokenConnectionReason_IsNullForActive_NoAccount_AndDeliberateDisconnect()
    {
        Assert.Null(MeetingProviderDecision.BrokenConnectionReason(null));
        Assert.Null(MeetingProviderDecision.BrokenConnectionReason(Account()));

        var disconnected = Account();
        disconnected.MarkRevoked(GoogleAccountRevokedReason.UserDisconnected, Clock);
        Assert.Null(MeetingProviderDecision.BrokenConnectionReason(disconnected));

        var revoked = Account();
        revoked.MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, Clock);
        Assert.Equal(GoogleAccountRevokedReason.InvalidGrant, MeetingProviderDecision.BrokenConnectionReason(revoked));
    }
}
