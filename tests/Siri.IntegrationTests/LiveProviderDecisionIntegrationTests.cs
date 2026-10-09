using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.Integrations.Google;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// The provider decision made in the request that creates the class, with NO background worker running (the test host has none): a room that cannot be
/// built automatically is committed at once as "paste a link" in the same transaction as the class, so the instructor's screen and the publish gate are right
/// immediately; a room that needs Google stays Pending for the job. Everything goes through the real endpoints and the real database.
/// <para>Requires Docker like every test in this collection (or the local-services recipe in <c>ExternalTestServices</c>).</para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveProviderDecisionIntegrationTests : IAsyncLifetime
{
    private const string ManualUrl = "https://meet.google.com/abc-defg-hij";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly List<Guid> _courses = [];

    public LiveProviderDecisionIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);
        await MigrateAsync(_factory);
        _client = _factory.CreateClient();
    }

    /// <summary>A live course owned by the instructor, remembered so the class can retire it afterwards (see <see cref="LiveIntegrationSupport.RetireCoursesAsync"/>).</summary>
    private async Task<(Guid CourseId, string Slug)> NewCourseAsync(Guid instructorProfileId)
    {
        var course = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructorProfileId);
        _courses.Add(course.CourseId);
        return course;
    }

    public async Task DisposeAsync()
    {
        await LiveIntegrationSupport.RetireCoursesAsync(_factory, _courses);
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    private async Task<SESSION_MEETING> MeetingOfAsync(Guid sessionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SessionMeetings().AsNoTracking().SingleAsync(m => m.SESSION_ID == sessionId);
    }

    [Fact]
    public async Task CreateSession_WithoutGoogle_IsAwaitingLinkAtOnce_WithNoJob_AndTheUiSaysPasteTheLink()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await NewCourseAsync(instructor.ProfileId);

        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        var meeting = await MeetingOfAsync(sessionId);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Equal(MeetingProvider.Manual, meeting.PROVIDER);
        Assert.Equal(instructor.UserId, meeting.INSTRUCTOR_USER_ID);
        Assert.Null(meeting.MEET_URL_ENCRYPTED);

        var (status, body) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Get, $"/api/live/instructor/courses/{courseId}/meetings", instructor.Token));
        Assert.Equal(HttpStatusCode.OK, status);
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("AwaitingLink", item.GetProperty("syncStatus").GetString());
        Assert.Equal("PasteLink", item.GetProperty("needsAction").GetString()); // not "Waiting" forever
    }

    [Fact]
    public async Task ThePublishGate_WorksWithNoWorker_PasteALink_ThenSubmit()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await NewCourseAsync(instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        // Before: the gate sees a real, final state ("no usable room") - not a transient "still pending" that only a worker could resolve.
        var (blocked, blockedBody) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/submit-for-review", instructor.Token));
        Assert.Equal(HttpStatusCode.BadRequest, blocked);
        Assert.Equal("live.meetings_not_ready", blockedBody.RootElement.GetProperty("reason").GetString());

        var (linked, _) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, ManualUrl));
        Assert.Equal(HttpStatusCode.OK, linked);

        var meeting = await MeetingOfAsync(sessionId);
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.True(meeting.IsUsable);
    }

    [Fact]
    public async Task UpdateSession_KeepsTheAwaitingLinkDecision_AndBumpsTheCalendarSequence()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await NewCourseAsync(instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 3);

        var start = DateTime.UtcNow.AddDays(3).AddHours(5);
        using var update = LiveIntegrationSupport.Authorized(HttpMethod.Put, $"/api/catalog/instructor/courses/{courseId}/live-sessions/{sessionId}", instructor.Token);
        update.Content = JsonContent.Create(new { title = "เลื่อนเวลา", description = (string?)null, startsAtUtc = start, endsAtUtc = start.AddHours(2) });
        using var response = await _client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var meeting = await MeetingOfAsync(sessionId);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
    }

    [Fact]
    public async Task CreateSession_WithAnActiveGoogleAccount_StaysPending_ForTheJobToCallGoogle()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        await SeedGoogleAccountAsync(instructor.UserId, revokedReason: null);
        var (courseId, _) = await NewCourseAsync(instructor.ProfileId);

        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        var meeting = await MeetingOfAsync(sessionId);
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Null(meeting.PROVIDER);
        Assert.Equal(instructor.UserId, meeting.INSTRUCTOR_USER_ID);
    }

    [Fact]
    public async Task CreateSession_WithABrokenGoogleConnection_IsNeedsReconnect()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        await SeedGoogleAccountAsync(instructor.UserId, revokedReason: GoogleAccountRevokedReason.InvalidGrant);
        var (courseId, _) = await NewCourseAsync(instructor.ProfileId);

        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        var meeting = await MeetingOfAsync(sessionId);
        Assert.Equal(MeetingSyncStatus.NeedsReconnect, meeting.SYNC_STATUS);
        Assert.Equal(GoogleAccountRevokedReason.InvalidGrant, meeting.ERROR);
    }

    [Fact]
    public async Task ARoomDecidedAtCreation_IsFoundByAGoogleReconnect_BecauseTheInstructorWasRecorded()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await NewCourseAsync(instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var resettable = await scope.ServiceProvider.GetRequiredService<ISessionMeetingRepository>()
            .GetResettableByInstructorAsync(instructor.UserId, CancellationToken.None);

        Assert.Contains(resettable, m => m.SESSION_ID == sessionId);
    }

    [Fact]
    public async Task TheJobStaysTheReconciler_ForARowThatIsStillPending()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await NewCourseAsync(instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        // Put the row back to the old "staged, undecided" state, as if it had been created before this change.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var meeting = await db.SessionMeetings().SingleAsync(m => m.SESSION_ID == sessionId);
            meeting.RequestResync(); // AwaitingLink -> Pending, provider undecided
            await db.SaveChangesAsync();
        }

        Assert.Equal(MeetingSyncStatus.Pending, (await MeetingOfAsync(sessionId)).SYNC_STATUS);

        await RunJobUntilProcessedAsync(sessionId);

        var decided = await MeetingOfAsync(sessionId);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, decided.SYNC_STATUS); // the job reaches the very same conclusion
        Assert.Equal(MeetingProvider.Manual, decided.PROVIDER);
    }

    private async Task RunJobUntilProcessedAsync(Guid sessionId)
    {
        for (var run = 0; run < 40; run++)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LiveMeetingSyncJob>().RunAsync(CancellationToken.None);

            var meeting = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SessionMeetings().AsNoTracking().SingleAsync(m => m.SESSION_ID == sessionId);
            if (meeting.SYNC_STATUS is not (MeetingSyncStatus.Pending or MeetingSyncStatus.PendingDelete))
            {
                return;
            }
        }

        Assert.Fail("The sync job never processed the session's meeting.");
    }

    private async Task SeedGoogleAccountAsync(Guid instructorUserId, string? revokedReason)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var protector = scope.ServiceProvider.GetRequiredService<ISensitiveDataProtector>();

        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            instructorUserId, $"sub-{Guid.NewGuid():N}", "teacher@gmail.test", protector.Encrypt("refresh-token"), GoogleScopes.CalendarEventsOwned, clock);
        if (revokedReason is not null)
        {
            account.MarkRevoked(revokedReason, clock);
        }

        db.InstructorGoogleAccounts().Add(account);
        await db.SaveChangesAsync();
    }
}

/// <summary><c>Live:Provider=ManualOnly</c>: Google is never involved, so every class is "paste a link" the moment it is created.</summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveManualOnlyDecisionIntegrationTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly List<Guid> _courses = [];

    public LiveManualOnlyDecisionIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers, new Dictionary<string, string?> { ["Live:Provider"] = "ManualOnly" });

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    /// <summary>A live course owned by the instructor, remembered so the class can retire it afterwards (see <see cref="LiveIntegrationSupport.RetireCoursesAsync"/>).</summary>
    private async Task<(Guid CourseId, string Slug)> NewCourseAsync(Guid instructorProfileId)
    {
        var course = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructorProfileId);
        _courses.Add(course.CourseId);
        return course;
    }

    public async Task DisposeAsync()
    {
        await LiveIntegrationSupport.RetireCoursesAsync(_factory, _courses);
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task EvenWithAnActiveGoogleAccount_ManualOnlyNeverWaitsForGoogle()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            db.InstructorGoogleAccounts().Add(INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
                instructor.UserId, $"sub-{Guid.NewGuid():N}", "teacher@gmail.test", "enc", GoogleScopes.CalendarEventsOwned, clock));
            await db.SaveChangesAsync();
        }

        var (courseId, _) = await NewCourseAsync(instructor.ProfileId);

        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        await using var read = _factory.Services.CreateAsyncScope();
        var meeting = await read.ServiceProvider.GetRequiredService<AppDbContext>().SessionMeetings().AsNoTracking().SingleAsync(m => m.SESSION_ID == sessionId);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Equal(MeetingProvider.Manual, meeting.PROVIDER);
    }
}
