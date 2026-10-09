using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// <c>GET /api/live/admin/status</c> over real HTTP against the production composition root (<see cref="SiriApiFactory"/>: real PostgreSQL + Redis): who may
/// call it, the exact JSON shape, the numbers it reads from the real tables, the warnings, the per-user rate limit, and that nothing secret is in the answer.
/// The factory runs with the default <c>Hangfire:ServerInApi=false</c> (no background jobs in a test host), so "no job server" is the truthful state here;
/// the opposite is proven in <see cref="HangfireHostingIntegrationTests"/>.
/// <para>Requires Docker like every test in this collection (or the local-services recipe in <c>ExternalTestServices</c>).</para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveAdminStatusIntegrationTests : IAsyncLifetime
{
    private const string StatusUrl = "/api/live/admin/status";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly List<Guid> _courses = [];

    public LiveAdminStatusIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

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

    private async Task<JsonDocument> GetStatusAsync(string token)
    {
        var (status, body) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.Authorized(HttpMethod.Get, StatusUrl, token));
        Assert.Equal(HttpStatusCode.OK, status);
        return body;
    }

    // ---- Who may call it ---------------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_Gets401()
    {
        using var response = await _client.GetAsync(StatusUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(ROLE.LearnerName)]
    [InlineData(ROLE.InstructorName)]
    public async Task ANonAdministrator_Gets403(string role)
    {
        var (_, token) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, role);

        using var response = await _client.SendAsync(LiveIntegrationSupport.Authorized(HttpMethod.Get, StatusUrl, token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(ROLE.AdminName)]
    [InlineData(ROLE.SuperAdminName)]
    public async Task AnAdministrator_Gets200_NoStore(string role)
    {
        var (_, token) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, role);

        using var response = await _client.SendAsync(LiveIntegrationSupport.Authorized(HttpMethod.Get, StatusUrl, token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task OnlyGetIsAllowed()
    {
        var (_, token) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.AdminName);

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            using var response = await _client.SendAsync(LiveIntegrationSupport.Authorized(method, StatusUrl, token));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    // ---- The answer ----------------------------------------------------------------------------------------

    [Fact]
    public async Task TheAnswer_HasTheContractedShape_AndTellsTheTruthAboutATestHostWithNoJobServer()
    {
        var (_, token) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.AdminName);

        using var body = await GetStatusAsync(token);
        var root = body.RootElement;

        Assert.Equal(
            new[] { "email", "jobServer", "live", "recurringJobs", "serverTimeUtc", "warnings" },
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.InRange((DateTime.UtcNow - root.GetProperty("serverTimeUtc").GetDateTime().ToUniversalTime()).TotalSeconds, -5, 30);

        // Nothing is processing background jobs in this host.
        var jobServer = root.GetProperty("jobServer");
        Assert.False(jobServer.GetProperty("running").GetBoolean());
        Assert.Equal(0, jobServer.GetProperty("serverCount").GetInt32());
        Assert.Empty(jobServer.GetProperty("servers").EnumerateArray());
        Assert.Empty(root.GetProperty("recurringJobs").EnumerateArray());

        var warnings = root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Contains("no_job_server", warnings);
        Assert.Contains("recurring_jobs_missing", warnings);
        Assert.Contains("email_unconfigured", warnings); // the test host runs Email:Provider=Log
        Assert.Contains("google_not_configured", warnings); // Live:Provider defaults to GoogleMeet but no OAuth client is configured

        var email = root.GetProperty("email");
        Assert.Equal("Log", email.GetProperty("provider").GetString());
        Assert.True(email.GetProperty("pendingCount").GetInt32() >= 0);
        Assert.True(email.GetProperty("failedCount").GetInt32() >= 0);

        var live = root.GetProperty("live");
        Assert.Equal("GoogleMeet", live.GetProperty("provider").GetString());
        Assert.False(live.GetProperty("googleConfigured").GetBoolean());
        Assert.Equal("siriupskill.siristudiophoto.com", live.GetProperty("publicBaseUrl").GetString()); // host only: no scheme, no path
    }

    [Fact]
    public async Task TheAnswer_NeverContainsASecret_AnAddress_OrAConnectionString()
    {
        var (admin, token) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.AdminName);
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await NewCourseAsync(instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);
        await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, "https://zoom.us/j/555555?pwd=VERYSECRETPASSCODE"));

        using var response = await _client.SendAsync(LiveIntegrationSupport.Authorized(HttpMethod.Get, StatusUrl, token));
        var text = await response.Content.ReadAsStringAsync();

        var connection = new Npgsql.NpgsqlConnectionStringBuilder(_containers.SqlConnectionString);
        foreach (var forbidden in new[]
                 {
                     "VERYSECRETPASSCODE", "zoom.us/j", "meet.invalid", admin.Email, "@example.test", "Bearer ", token,
                     connection.Password!, connection.Username!, "Password=", "Host=",
                     "eyJ", // a JWT
                 })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }

        using var doc = JsonDocument.Parse(text);
        LiveIntegrationSupport.AssertNoRoomUrl(doc, "VERYSECRETPASSCODE");
    }

    [Fact]
    public async Task TheLiveCounters_ReadTheRealTables()
    {
        var (_, token) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.AdminName);
        var before = await ReadLiveCountersAsync(token);

        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await NewCourseAsync(instructor.ProfileId);
        await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 2);
        await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 4);

        var after = await ReadLiveCountersAsync(token);

        // Google is off in this host, so each new class is decided at once as "paste a link" - no job needed.
        Assert.Equal(before.AwaitingLink + 2, after.AwaitingLink);
        Assert.Equal(before.Pending, after.Pending);
    }

    private async Task<(int Pending, int AwaitingLink)> ReadLiveCountersAsync(string token)
    {
        using var body = await GetStatusAsync(token);
        var live = body.RootElement.GetProperty("live");
        return (live.GetProperty("meetingsPending").GetInt32(), live.GetProperty("meetingsAwaitingLink").GetInt32());
    }

    [Fact]
    public async Task TheEmailCounters_ReadTheRealOutbox_IncludingTheAgeOfTheOldestWaitingMessage()
    {
        var (_, token) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.AdminName);
        var before = await ReadEmailAsync(token);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            db.EmailOutboxMessages().Add(EMAIL_OUTBOX_MESSAGE.Enqueue("waiting@example.test", "queued", "<p>x</p>", "admin-status-test"));

            var abandoned = EMAIL_OUTBOX_MESSAGE.Enqueue("abandoned@example.test", "gave up", "<p>x</p>", "admin-status-test");
            for (var attempt = 0; attempt < EMAIL_OUTBOX_MESSAGE.MaxAttempts; attempt++)
            {
                abandoned.RecordAttemptFailed("smtp refused", clock);
            }

            db.EmailOutboxMessages().Add(abandoned);
            await db.SaveChangesAsync();
        }

        var after = await ReadEmailAsync(token);

        Assert.Equal(before.Pending + 1, after.Pending);
        Assert.Equal(before.Failed + 1, after.Failed);
        Assert.NotNull(after.OldestAgeSeconds);
        Assert.True(after.OldestAgeSeconds >= 0);
    }

    private async Task<(int Pending, int Failed, long? OldestAgeSeconds)> ReadEmailAsync(string token)
    {
        using var body = await GetStatusAsync(token);
        var email = body.RootElement.GetProperty("email");
        var age = email.GetProperty("oldestPendingAgeSeconds");
        return (email.GetProperty("pendingCount").GetInt32(), email.GetProperty("failedCount").GetInt32(), age.ValueKind == JsonValueKind.Null ? null : age.GetInt64());
    }

    // ---- Rate limit ------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheRateLimit_IsPerUser_OneAdminExhaustingTheirQuotaDoesNotAffectAnother()
    {
        var (_, first) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.AdminName);
        var (_, second) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.AdminName);

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 70 && last != HttpStatusCode.TooManyRequests; i++)
        {
            using var response = await _client.SendAsync(LiveIntegrationSupport.Authorized(HttpMethod.Get, StatusUrl, first));
            last = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);

        using var other = await _client.SendAsync(LiveIntegrationSupport.Authorized(HttpMethod.Get, StatusUrl, second));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }
}
