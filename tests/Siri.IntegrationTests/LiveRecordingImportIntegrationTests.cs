using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.Integrations.Google;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Modules.Media.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// P11-13 (docs/contracts/P11-13-live-recording-auto-import.md section 10, integration part) against the production composition root
/// (<see cref="SiriApiFactory"/>: real PostgreSQL + Redis) with <c>Live:Provider=Logging</c> and the import switched on: the migration's table and indexes,
/// the import-row repository (one row per session, the due query, optimistic concurrency), the instructor endpoints' ownership matrix (status, recording-access
/// connect, retry, <c>recordingImport</c> in the session list), and the job end to end up to — but not including — the video provider (which is not configured in
/// tests: the copy fails cleanly and nothing is left behind).
/// <para>
/// Access tokens are minted with the real <see cref="IAccessTokenGenerator"/> (the "auth" limiter allows only five logins a minute). Requires Docker like
/// every test in this collection — without it these end in <c>DockerUnavailableException</c> (not an assertion); they have not been run on a machine without it.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveRecordingImportIntegrationTests : IAsyncLifetime
{
    private const string PublicBaseUrl = "https://app.example.test";
    private const string WorkspaceEmail = "teacher@workspace.example.test";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;
    private HttpClient _noRedirect = null!;

    public LiveRecordingImportIntegrationTests(ContainersFixture containers)
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
            ["Integrations:Google:DevAccountEmail"] = WorkspaceEmail,
            ["Live:Recording:AutoImport:Enabled"] = "true",
            // The database is shared with the other tests of the collection; a big batch makes sure this class's own rows are reached in one run.
            ["Live:Recording:AutoImport:BatchSize"] = "100",
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

    // ---- Arrange helpers ------------------------------------------------------------------------------------

    private sealed record Actor(Guid UserId, string Token, Guid ProfileId = default);

    private async Task<Actor> CreateLearnerAsync()
    {
        var builder = new TestUserBuilder().WithRole(ROLE.LearnerName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);
        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, Guid.NewGuid());
        return new Actor(user.Id, token);
    }

    private async Task<Actor> CreateInstructorAsync()
    {
        var builder = new TestUserBuilder().WithRole(ROLE.InstructorName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Test Instructor", "Headline", "Bio");
        profile.Approve(clock);
        db.InstructorProfiles().Add(profile);
        await db.SaveChangesAsync();

        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, Guid.NewGuid());
        return new Actor(user.Id, token, profile.Id);
    }

    /// <summary>A Live course with one session created through the real endpoint (future, as the domain demands).</summary>
    private async Task<(Guid CourseId, Guid SessionId)> CreateSessionAsync(Actor instructor)
    {
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 2);
        return (courseId, sessionId);
    }

    /// <summary>Rewrites a session's window directly — the domain refuses to schedule into the past.</summary>
    private async Task MoveSessionAsync(Guid sessionId, DateTime startsAtUtc)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var session = await db.CourseLiveSessions().SingleAsync(s => s.Id == sessionId);
        db.Entry(session).Property(s => s.StartsAtUtc).CurrentValue = startsAtUtc;
        db.Entry(session).Property(s => s.EndsAtUtc).CurrentValue = startsAtUtc.AddHours(2);
        await db.SaveChangesAsync();
    }

    private async Task<string> BeginAsync(string path, string token)
    {
        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Post, path, token);
        request.Content = JsonContent.Create(new { returnPath = "/instructor/live-settings" });

        var (status, body) = await LiveIntegrationSupport.SendAsync(_client, request);
        Assert.Equal(HttpStatusCode.OK, status);

        var url = new Uri(body.RootElement.GetProperty("authorizationUrl").GetString()!);
        return HttpUtility.ParseQueryString(url.Query)["state"]!;
    }

    private async Task<HttpResponseMessage> CallbackAsync(string state, string code) =>
        await _noRedirect.GetAsync($"/api/live/instructor/google/callback?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");

    /// <summary>Connects the instructor's (fake, Workspace) Google account and grants the recording access, through the real endpoints.</summary>
    private async Task ConnectWithRecordingAccessAsync(Actor instructor)
    {
        var state = await BeginAsync("/api/live/instructor/google/connect", instructor.Token);
        using (var connected = await CallbackAsync(state, "dev"))
        {
            Assert.EndsWith("?google=connected", connected.Headers.Location!.ToString());
        }

        var recordingState = await BeginAsync("/api/live/instructor/google/recording-access/connect", instructor.Token);
        using var granted = await CallbackAsync(recordingState, "dev-recording");
        Assert.EndsWith("?google=connected", granted.Headers.Location!.ToString());
    }

    private async Task<JsonDocument> GetJsonAsync(string path, string token)
    {
        var (status, body) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.Authorized(HttpMethod.Get, path, token));
        Assert.Equal(HttpStatusCode.OK, status);
        return body;
    }

    private async Task<SESSION_RECORDING_IMPORT?> ReadImportAsync(Guid sessionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SessionRecordingImports().AsNoTracking()
            .SingleOrDefaultAsync(i => i.SESSION_ID == sessionId);
    }

    private async Task InsertImportAsync(Func<SESSION_RECORDING_IMPORT> create)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SessionRecordingImports().Add(create());
        await db.SaveChangesAsync();
    }

    private static SESSION_RECORDING_IMPORT NewImport(Guid? sessionId = null, Guid? instructorUserId = null, DateTime? nextAttemptAtUtc = null) =>
        SESSION_RECORDING_IMPORT.Create(
            sessionId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            instructorUserId ?? Guid.NewGuid(),
            nextAttemptAtUtc ?? DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddHours(10));

    // ---- The migration -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Migration_CreatesTheImportTableWithItsIndexes_AndTheTwoAccountColumns()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexes = await db.Database
            .SqlQuery<string>($"SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'LIVE' AND tablename = 'SESSION_RECORDING_IMPORTS'")
            .ToListAsync();
        Assert.Contains("PK_SESSION_RECORDING_IMPORTS", indexes);
        Assert.Contains("IX_SESSION_RECORDING_IMPORTS_SESSION_ID", indexes);
        Assert.Contains("IX_SESSION_RECORDING_IMPORTS_DUE", indexes);

        var accountColumns = await db.Database
            .SqlQuery<string>($"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'LIVE' AND table_name = 'INSTRUCTOR_GOOGLE_ACCOUNTS'")
            .ToListAsync();
        Assert.Contains("HOSTED_DOMAIN", accountColumns);
        Assert.Contains("ACCOUNT_KIND_CHECKED_AT_UTC", accountColumns);
    }

    // ---- The repository ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Repository_RoundTripsARow_WithTheEnumAsAStringAndUtcTimes()
    {
        var sessionId = Guid.NewGuid();
        var import = NewImport(sessionId);
        import.BeginTransfer("conferenceRecords/a/recordings/b", "file-1", DateTime.UtcNow.AddHours(3));
        await InsertImportAsync(() => import);

        var stored = await ReadImportAsync(sessionId);

        Assert.NotNull(stored);
        Assert.Equal(RecordingImportStatus.Transferring, stored.STATUS);
        Assert.Equal("file-1", stored.GOOGLE_FILE_ID);
        Assert.Equal(DateTimeKind.Utc, stored.LEASE_UNTIL_UTC!.Value.Kind);
        Assert.NotEmpty(stored.ROW_VERSION);

        await using var scope = _factory.Services.CreateAsyncScope();
        var raw = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<string>($"SELECT \"STATUS\" AS \"Value\" FROM \"LIVE\".\"SESSION_RECORDING_IMPORTS\" WHERE \"SESSION_ID\" = {sessionId}")
            .SingleAsync();
        Assert.Equal("Transferring", raw);
    }

    [Fact]
    public async Task Repository_OneImportPerSession_TheSecondInsertIsRefusedByTheUniqueIndex()
    {
        var sessionId = Guid.NewGuid();
        await InsertImportAsync(() => NewImport(sessionId));

        await Assert.ThrowsAsync<DbUpdateException>(() => InsertImportAsync(() => NewImport(sessionId)));

        await using var scope = _factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISessionRecordingImportRepository>();
        Assert.Single(await repository.GetBySessionIdsAsync([sessionId], CancellationToken.None));
        Assert.Contains(sessionId, await repository.GetExistingSessionIdsAsync([sessionId, Guid.NewGuid()], CancellationToken.None));
    }

    [Fact]
    public async Task Repository_DueQuery_ReturnsOnlyWhatTheJobMayTouchNow()
    {
        var now = DateTime.UtcNow;
        var waitingDue = NewImport(nextAttemptAtUtc: now.AddMinutes(-1));
        var waitingLater = NewImport(nextAttemptAtUtc: now.AddHours(1));

        var processingDue = NewImport();
        processingDue.BeginTransfer("n", "f", now.AddHours(1));
        processingDue.MarkProcessing(Guid.NewGuid(), now.AddMinutes(-1), now.AddHours(5));

        var transferringExpired = NewImport();
        transferringExpired.BeginTransfer("n", "f", now.AddMinutes(-1));

        var transferringLeased = NewImport();
        transferringLeased.BeginTransfer("n", "f", now.AddHours(1));

        var failed = NewImport();
        failed.MarkFailed("file_too_large", new FixedClock(now));

        foreach (var row in new[] { waitingDue, waitingLater, processingDue, transferringExpired, transferringLeased, failed })
        {
            await InsertImportAsync(() => row);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var due = await scope.ServiceProvider.GetRequiredService<ISessionRecordingImportRepository>().GetDueIdsAsync(now, 10_000, CancellationToken.None);

        Assert.Contains(waitingDue.SESSION_RECORDING_IMPORT_ID, due);
        Assert.Contains(processingDue.SESSION_RECORDING_IMPORT_ID, due);
        Assert.Contains(transferringExpired.SESSION_RECORDING_IMPORT_ID, due);
        Assert.DoesNotContain(waitingLater.SESSION_RECORDING_IMPORT_ID, due);
        Assert.DoesNotContain(transferringLeased.SESSION_RECORDING_IMPORT_ID, due);
        Assert.DoesNotContain(failed.SESSION_RECORDING_IMPORT_ID, due);
    }

    [Fact]
    public async Task Repository_TwoRunsCannotBothClaimTheSameRow()
    {
        var import = NewImport();
        await InsertImportAsync(() => import);

        await using var first = _factory.Services.CreateAsyncScope();
        await using var second = _factory.Services.CreateAsyncScope();
        var firstRepo = first.ServiceProvider.GetRequiredService<ISessionRecordingImportRepository>();
        var secondRepo = second.ServiceProvider.GetRequiredService<ISessionRecordingImportRepository>();

        var a = await firstRepo.GetByIdAsync(import.SESSION_RECORDING_IMPORT_ID, CancellationToken.None);
        var b = await secondRepo.GetByIdAsync(import.SESSION_RECORDING_IMPORT_ID, CancellationToken.None);
        a!.BeginTransfer("n", "f1", DateTime.UtcNow.AddHours(3));
        b!.BeginTransfer("n", "f2", DateTime.UtcNow.AddHours(3));

        await firstRepo.SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondRepo.SaveChangesAsync(CancellationToken.None));
    }

    // ---- Status and the recording-access consent ------------------------------------------------------------------------

    [Fact]
    public async Task Status_BeforeAnyConnection_HasNoAccountKind_AndTheFeatureFlagShowsThroughIt()
    {
        var instructor = await CreateInstructorAsync();

        using var body = await GetJsonAsync("/api/live/instructor/google/status", instructor.Token);

        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("accountKind").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("hostedDomain").ValueKind);
        var recording = body.RootElement.GetProperty("recording");
        Assert.Equal("Manual", recording.GetProperty("mode").GetString());
        Assert.True(recording.GetProperty("autoImportAvailable").GetBoolean());
        Assert.False(recording.GetProperty("scopesGranted").GetBoolean());
    }

    [Fact]
    public async Task RecordingAccessConnect_WithoutAConnectedAccount_Is409NotAvailable()
    {
        var instructor = await CreateInstructorAsync();

        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Post, "/api/live/instructor/google/recording-access/connect", instructor.Token);
        request.Content = JsonContent.Create(new { returnPath = "/instructor/live-settings" });
        var (status, body) = await LiveIntegrationSupport.SendAsync(_client, request);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("live.recording_not_available", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RecordingAccessConnect_IsInstructorOnly()
    {
        var learner = await CreateLearnerAsync();

        using var anonymous = new HttpRequestMessage(HttpMethod.Post, "/api/live/instructor/google/recording-access/connect") { Content = JsonContent.Create(new { }) };
        using var anonymousResponse = await _client.SendAsync(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var asLearner = LiveIntegrationSupport.Authorized(HttpMethod.Post, "/api/live/instructor/google/recording-access/connect", learner.Token);
        asLearner.Content = JsonContent.Create(new { });
        using var learnerResponse = await _client.SendAsync(asLearner);
        Assert.Equal(HttpStatusCode.Forbidden, learnerResponse.StatusCode);
    }

    [Fact]
    public async Task TheFullConsentFlow_TakesAWorkspaceAccountFromManualToAutoNeedsConsentToAuto()
    {
        var instructor = await CreateInstructorAsync();

        var state = await BeginAsync("/api/live/instructor/google/connect", instructor.Token);
        using (await CallbackAsync(state, "dev"))
        {
        }

        using (var afterConnect = await GetJsonAsync("/api/live/instructor/google/status", instructor.Token))
        {
            Assert.True(afterConnect.RootElement.GetProperty("connected").GetBoolean());
            Assert.Equal("Workspace", afterConnect.RootElement.GetProperty("accountKind").GetString());
            Assert.Equal("workspace.example.test", afterConnect.RootElement.GetProperty("hostedDomain").GetString());
            Assert.Equal("AutoNeedsConsent", afterConnect.RootElement.GetProperty("recording").GetProperty("mode").GetString());
            Assert.False(afterConnect.RootElement.GetProperty("recording").GetProperty("scopesGranted").GetBoolean());
        }

        var recordingState = await BeginAsync("/api/live/instructor/google/recording-access/connect", instructor.Token);
        using (var granted = await CallbackAsync(recordingState, "dev-recording"))
        {
            Assert.EndsWith("?google=connected", granted.Headers.Location!.ToString());
        }

        using var afterConsent = await GetJsonAsync("/api/live/instructor/google/status", instructor.Token);
        Assert.Equal("Auto", afterConsent.RootElement.GetProperty("recording").GetProperty("mode").GetString());
        Assert.True(afterConsent.RootElement.GetProperty("recording").GetProperty("scopesGranted").GetBoolean());

        await using var scope = _factory.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<AppDbContext>().InstructorGoogleAccounts().AsNoTracking()
            .SingleAsync(a => a.INSTRUCTOR_USER_ID == instructor.UserId);
        Assert.True(account.HasRecordingScopes);
        Assert.Equal("workspace.example.test", account.HOSTED_DOMAIN);
    }

    [Fact]
    public async Task TheRecordingConsentCallback_WithAnOrdinaryCode_StoresNothing_BecauseTheRecordingScopesAreMissing()
    {
        var instructor = await CreateInstructorAsync();
        var state = await BeginAsync("/api/live/instructor/google/connect", instructor.Token);
        using (await CallbackAsync(state, "dev"))
        {
        }

        var recordingState = await BeginAsync("/api/live/instructor/google/recording-access/connect", instructor.Token);
        using var denied = await CallbackAsync(recordingState, "dev"); // a consent that did not include the recording scopes

        Assert.EndsWith("?google=error&reason=recording_scope_missing", denied.Headers.Location!.ToString());
    }

    // ---- Retry ----------------------------------------------------------------------------------------------------------

    private async Task<(HttpStatusCode Status, JsonDocument Body)> RetryAsync(Guid sessionId, string? token)
    {
        var request = LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/live/instructor/sessions/{sessionId}/recording-import/retry", token);
        return await LiveIntegrationSupport.SendAsync(_client, request);
    }

    [Fact]
    public async Task Retry_IsAuthenticatedAndInstructorOnly()
    {
        var learner = await CreateLearnerAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await RetryAsync(Guid.NewGuid(), token: null)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await RetryAsync(Guid.NewGuid(), learner.Token)).Status);
    }

    [Fact]
    public async Task Retry_UnknownSession_Is404()
    {
        var instructor = await CreateInstructorAsync();

        var (status, _) = await RetryAsync(Guid.NewGuid(), instructor.Token);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Retry_SomeoneElsesSession_Is403_AndTheirImportIsUntouched()
    {
        var owner = await CreateInstructorAsync();
        var intruder = await CreateInstructorAsync();
        var (courseId, sessionId) = await CreateSessionAsync(owner);
        await MoveSessionAsync(sessionId, DateTime.UtcNow.AddHours(-5));
        var failed = SESSION_RECORDING_IMPORT.Create(sessionId, courseId, owner.UserId, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(9));
        failed.MarkFailed("file_too_large", new FixedClock(DateTime.UtcNow));
        await InsertImportAsync(() => failed);

        var (status, _) = await RetryAsync(sessionId, intruder.Token);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(RecordingImportStatus.Failed, (await ReadImportAsync(sessionId))!.STATUS);
    }

    [Fact]
    public async Task Retry_TheOwnersFailedImport_GoesBackToWaiting_AndAnswersWithTheNewState()
    {
        var owner = await CreateInstructorAsync();
        var (courseId, sessionId) = await CreateSessionAsync(owner);
        await MoveSessionAsync(sessionId, DateTime.UtcNow.AddHours(-5));
        var failed = SESSION_RECORDING_IMPORT.Create(sessionId, courseId, owner.UserId, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(9));
        failed.MarkFailed("file_too_large", new FixedClock(DateTime.UtcNow));
        await InsertImportAsync(() => failed);

        var (status, body) = await RetryAsync(sessionId, owner.Token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Auto", body.RootElement.GetProperty("mode").GetString());
        Assert.Equal("Waiting", body.RootElement.GetProperty("status").GetString());
        Assert.False(body.RootElement.GetProperty("canRetry").GetBoolean());
        var stored = await ReadImportAsync(sessionId);
        Assert.Equal(RecordingImportStatus.Waiting, stored!.STATUS);
        Assert.Equal(0, stored.ATTEMPTS);
        Assert.Null(stored.ERROR_CODE);

        // Nothing but codes and times: never an id of Google's or the video provider's.
        LiveIntegrationSupport.AssertNoRoomUrl(body, "SECRET");
    }

    [Fact]
    public async Task Retry_WithNothingToRetry_Is409_WithTheStableReason()
    {
        var owner = await CreateInstructorAsync(); // no Google account at all: the manual path
        var (_, sessionId) = await CreateSessionAsync(owner);
        await MoveSessionAsync(sessionId, DateTime.UtcNow.AddHours(-5));

        var (status, body) = await RetryAsync(sessionId, owner.Token);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("live.recording_import_not_retryable", body.RootElement.GetProperty("reason").GetString());
        Assert.Null(await ReadImportAsync(sessionId));
    }

    // ---- The job end to end (up to the video provider) -------------------------------------------------------------------------

    [Fact]
    public async Task Job_DiscoversTheEndedClass_FindsTheFakeRecording_AndTheUnconfiguredVideoProviderFailsCleanly()
    {
        var instructor = await CreateInstructorAsync();
        await ConnectWithRecordingAccessAsync(instructor);
        var (_, sessionId) = await CreateSessionAsync(instructor);

        // The room is made by the real sync job while the class is still ahead (Logging mode: a fake Meet link), then the class "happens".
        for (var run = 0; run < 40; run++)
        {
            await using var syncScope = _factory.Services.CreateAsyncScope();
            await syncScope.ServiceProvider.GetRequiredService<LiveMeetingSyncJob>().RunAsync(CancellationToken.None);

            var meeting = await syncScope.ServiceProvider.GetRequiredService<AppDbContext>().SessionMeetings().AsNoTracking().SingleAsync(m => m.SESSION_ID == sessionId);
            if (meeting.SYNC_STATUS is not (MeetingSyncStatus.Pending or MeetingSyncStatus.PendingDelete))
            {
                break;
            }
        }

        await MoveSessionAsync(sessionId, DateTime.UtcNow.AddHours(-5));

        // The recurring tick: finds the recording, CLAIMS the import and queues a background job for the copy - it never copies anything itself.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<LiveRecordingImportJob>().RunAsync(CancellationToken.None);
        }

        var claimed = await ReadImportAsync(sessionId);
        Assert.NotNull(claimed);
        Assert.Equal(RecordingImportStatus.Transferring, claimed.STATUS);
        Assert.NotNull(claimed.LEASE_UNTIL_UTC);
        Assert.Equal(instructor.UserId, claimed.INSTRUCTOR_USER_ID);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            // Exactly one transfer job for this import sits in Hangfire storage (no server runs in the test host, so it is still queued).
            var queued = scope.ServiceProvider.GetRequiredService<Hangfire.JobStorage>().GetMonitoringApi().EnqueuedJobs("default", 0, 1000)
                .Count(j => j.Value.Job is { } job && job.Type == typeof(LiveRecordingTransferJob) && job.Args.Count > 0 && job.Args[0] is Guid id && id == claimed.SESSION_RECORDING_IMPORT_ID);
            Assert.Equal(1, queued);

            // A second tick finds the row under its lease and queues nothing more.
            await scope.ServiceProvider.GetRequiredService<LiveRecordingImportJob>().RunAsync(CancellationToken.None);
            Assert.Equal(1, scope.ServiceProvider.GetRequiredService<Hangfire.JobStorage>().GetMonitoringApi().EnqueuedJobs("default", 0, 1000)
                .Count(j => j.Value.Job is { } job && job.Type == typeof(LiveRecordingTransferJob) && job.Args.Count > 0 && job.Args[0] is Guid id && id == claimed.SESSION_RECORDING_IMPORT_ID));
        }

        // The background job (what a Hangfire server would run).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<LiveRecordingTransferJob>().RunAsync(claimed.SESSION_RECORDING_IMPORT_ID, CancellationToken.None);
        }

        var row = await ReadImportAsync(sessionId);
        Assert.NotNull(row);
        // The fake Meet had a finished recording, the copy was attempted, and the video provider (not configured in tests) refused: back to Waiting for a retry.
        Assert.Equal(RecordingImportStatus.Waiting, row.STATUS);
        Assert.Equal(1, row.ATTEMPTS);
        Assert.Equal("ingest_failed", row.ERROR_CODE);
        Assert.Null(row.MEDIA_ASSET_ID);

        // Nothing is left behind at the video provider's side of the platform: no half-made asset for the instructor.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().MediaAssets().AnyAsync(a => a.UPLOADED_BY_USER_ID == instructor.UserId));
        }

        // And the instructor's list says so, in codes only.
        using var list = await GetJsonAsync("/api/live/instructor/sessions?scope=Past", instructor.Token);
        var item = list.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sessionId").GetGuid() == sessionId);
        var recordingImport = item.GetProperty("recordingImport");
        Assert.Equal("Auto", recordingImport.GetProperty("mode").GetString());
        Assert.Equal("Waiting", recordingImport.GetProperty("status").GetString());
        Assert.Equal("ingest_failed", recordingImport.GetProperty("errorCode").GetString());
        Assert.False(recordingImport.GetProperty("canRetry").GetBoolean());
    }

    [Fact]
    public async Task CatalogListing_AppliesTheInstructorFilterBeforeTheLimit_SoOtherInstructorsSessionsCannotCrowdItOut()
    {
        var crowd = await CreateInstructorAsync();
        var wanted = await CreateInstructorAsync();
        var (crowdCourse, first) = await CreateSessionAsync(crowd);
        var second = await LiveIntegrationSupport.CreateSessionAsync(_client, crowd.Token, crowdCourse, daysAhead: 4);
        var (_, theirs) = await CreateSessionAsync(wanted);
        await MoveSessionAsync(first, DateTime.UtcNow.AddHours(-30));
        await MoveSessionAsync(second, DateTime.UtcNow.AddHours(-20));
        await MoveSessionAsync(theirs, DateTime.UtcNow.AddHours(-10)); // the newest of the three

        await using var scope = _factory.Services.CreateAsyncScope();
        var attacher = scope.ServiceProvider.GetRequiredService<Siri.Modules.Catalog.Contracts.ILiveRecordingAttacher>();
        var from = DateTime.UtcNow.AddHours(-48);
        var to = DateTime.UtcNow;

        // One slot, and two older sessions of somebody else in front of it: the filter is part of the query, so the slot is still the wanted instructor's.
        var onlyHis = await attacher.ListEndedByInstructorsAsync([wanted.UserId], from, to, 1, CancellationToken.None);
        Assert.Equal(theirs, Assert.Single(onlyHis).SessionId);
        Assert.Equal(wanted.UserId, onlyHis[0].InstructorUserId);

        var both = await attacher.ListEndedByInstructorsAsync([crowd.UserId, wanted.UserId], from, to, 100, CancellationToken.None);
        Assert.Equal([first, second, theirs], both.Select(s => s.SessionId).ToArray()); // oldest first

        Assert.Empty(await attacher.ListEndedByInstructorsAsync([], from, to, 100, CancellationToken.None));
        Assert.Empty(await attacher.ListEndedByInstructorsAsync([Guid.NewGuid()], from, to, 100, CancellationToken.None));
    }

    [Fact]
    public async Task SessionList_ForAnInstructorOnTheManualPath_ShowsManual_WithNoStatus()
    {
        var instructor = await CreateInstructorAsync();
        var (_, sessionId) = await CreateSessionAsync(instructor);

        using var list = await GetJsonAsync("/api/live/instructor/sessions?scope=All", instructor.Token);

        var item = list.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sessionId").GetGuid() == sessionId);
        var recordingImport = item.GetProperty("recordingImport");
        Assert.Equal("Manual", recordingImport.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, recordingImport.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, recordingImport.GetProperty("errorCode").ValueKind);
        Assert.False(recordingImport.GetProperty("canRetry").GetBoolean());

        using var detail = await GetJsonAsync($"/api/live/instructor/sessions/{sessionId}", instructor.Token);
        Assert.Equal("Manual", detail.RootElement.GetProperty("recordingImport").GetProperty("mode").GetString());
    }

    /// <summary>
    /// N1 (P11-13 re-QA): two transfer jobs of one instructor each refresh the Google token and then do unrelated work on the same scoped <c>AppDbContext</c> (Media's ingest
    /// saves in the middle of the copy). The refresh used to leave <c>LAST_VALIDATED_AT_UTC</c> as a tracked, unsaved edit of the account, so the first scope's save won and the
    /// second scope's save - the same UPDATE with the old row version - failed with <see cref="DbUpdateConcurrencyException"/>. With a real database this is deterministic:
    /// both scopes load the row, both refresh, then both save.
    /// </summary>
    [Fact]
    public async Task TokenRefresh_InTwoScopesOfTheSameInstructor_LeavesNothingPending_SoBothScopesCanSaveAnythingElse()
    {
        var instructor = await CreateInstructorAsync();
        await ConnectWithRecordingAccessAsync(instructor);

        byte[] versionBefore;
        await using (var read = _factory.Services.CreateAsyncScope())
        {
            var before = await read.ServiceProvider.GetRequiredService<AppDbContext>().InstructorGoogleAccounts().AsNoTracking()
                .SingleAsync(a => a.INSTRUCTOR_USER_ID == instructor.UserId);
            versionBefore = before.ROW_VERSION;
        }

        await using var first = _factory.Services.CreateAsyncScope();
        await using var second = _factory.Services.CreateAsyncScope();

        // Both scopes load the row and refresh the token...
        var tokenA = await first.ServiceProvider.GetRequiredService<InstructorGoogleAccountService>().TryGetAccessTokenAsync(instructor.UserId, CancellationToken.None);
        var tokenB = await second.ServiceProvider.GetRequiredService<InstructorGoogleAccountService>().TryGetAccessTokenAsync(instructor.UserId, CancellationToken.None);
        Assert.True(tokenA.IsSuccess);
        Assert.True(tokenB.IsSuccess);

        var firstDb = first.ServiceProvider.GetRequiredService<AppDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.DoesNotContain(firstDb.ChangeTracker.Entries(), e => e.State is EntityState.Modified or EntityState.Added or EntityState.Deleted);
        Assert.DoesNotContain(secondDb.ChangeTracker.Entries(), e => e.State is EntityState.Modified or EntityState.Added or EntityState.Deleted);

        // ...and then each does some unrelated work on the shared context (a row of its own, parked far in the future so no job picks it up).
        firstDb.SessionRecordingImports().Add(NewImport(nextAttemptAtUtc: DateTime.UtcNow.AddDays(30)));
        secondDb.SessionRecordingImports().Add(NewImport(nextAttemptAtUtc: DateTime.UtcNow.AddDays(30)));
        await firstDb.SaveChangesAsync();
        await secondDb.SaveChangesAsync(); // used to throw DbUpdateConcurrencyException: the account UPDATE rode along with a stale row version

        await using var verify = _factory.Services.CreateAsyncScope();
        var after = await verify.ServiceProvider.GetRequiredService<AppDbContext>().InstructorGoogleAccounts().AsNoTracking()
            .SingleAsync(a => a.INSTRUCTOR_USER_ID == instructor.UserId);
        Assert.NotNull(after.LAST_VALIDATED_AT_UTC);
        Assert.Equal(versionBefore, after.ROW_VERSION); // the validation stamp is bookkeeping: it must not invalidate anybody else's copy of the row
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
