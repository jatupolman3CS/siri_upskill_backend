using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// D3 (integrator-qa) over real HTTP against the production composition root: a Meet/Zoom/Teams link typed into a live session's title, description or cancel reason is
/// refused with 400 + the stable reason <c>live.session_text_contains_meeting_link</c> (so it can never reach the PUBLIC course page or the learners' e-mails), ordinary text and
/// other links still go through, and a title stored before the rule existed is scrubbed in the anonymous course detail.
/// Requires Docker like every test in this collection — without it these end in <c>DockerUnavailableException</c> (not an assertion).
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveSessionMeetingLinkTextIntegrationTests : IAsyncLifetime
{
    private const string Reason = "live.session_text_contains_meeting_link";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;
    private TestInstructor _instructor = null!;

    public LiveSessionMeetingLinkTextIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        // Migrations are applied by the test, never by the app (database.md: no Database.Migrate() in Program.cs).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }

        _client = _factory.CreateClient();
        _instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static string Url(Guid courseId, Guid? sessionId = null, string suffix = "") =>
        $"/api/catalog/instructor/courses/{courseId}/live-sessions" + (sessionId is null ? string.Empty : $"/{sessionId}") + suffix;

    private async Task<(HttpStatusCode Status, JsonDocument Body)> PostSessionAsync(Guid courseId, string title, string? description)
    {
        var start = DateTime.UtcNow.AddDays(2);
        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Post, Url(courseId), _instructor.Token);
        request.Content = JsonContent.Create(new { title, description, startsAtUtc = start, endsAtUtc = start.AddHours(2) });
        return await LiveIntegrationSupport.SendAsync(_client, request);
    }

    private static void AssertRejectedForMeetingLink(HttpStatusCode status, JsonDocument body, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(Reason, body.RootElement.GetProperty("reason").GetString());
        Assert.Equal("validation", body.RootElement.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        Assert.Contains(body.RootElement.GetProperty("errors").EnumerateObject(), property => string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase));
    }

    // ---- Write side -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij")]
    [InlineData("เข้าห้องที่ https://zoom.us/j/987654321?pwd=SECRET ครับ")]
    [InlineData("meet.google.com/abc-defg-hij")] // scheme-less
    [InlineData("HTTPS://TEAMS.MICROSOFT.COM/l/meetup-join/x")] // case
    [InlineData("https%3A%2F%2Fmeet.google.com%2Fabc-defg-hij")] // encoded
    public async Task Create_MeetingLinkInTheTitleOrTheDescription_Is400WithTheStableReason(string text)
    {
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, _instructor.ProfileId);

        var (titleStatus, titleBody) = await PostSessionAsync(courseId, text, description: null);
        var (descriptionStatus, descriptionBody) = await PostSessionAsync(courseId, "คาบที่ 1", text);

        AssertRejectedForMeetingLink(titleStatus, titleBody, "title");
        AssertRejectedForMeetingLink(descriptionStatus, descriptionBody, "description");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.CourseLiveSessions().AsNoTracking().Where(s => s.CourseId == courseId).ToListAsync()); // nothing was stored
    }

    [Fact]
    public async Task Create_ZeroWidthAndTabInsideTheHost_AreRejectedToo()
    {
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, _instructor.ProfileId);
        var zeroWidthSpace = ((char)0x200B).ToString();

        var (zwStatus, zwBody) = await PostSessionAsync(courseId, $"meet.goo{zeroWidthSpace}gle.com/abc-defg-hij", description: null);
        var (tabStatus, tabBody) = await PostSessionAsync(courseId, "คาบที่ 1", "meet.goo\tgle.com/abc-defg-hij");

        AssertRejectedForMeetingLink(zwStatus, zwBody, "title");
        AssertRejectedForMeetingLink(tabStatus, tabBody, "description");
    }

    [Fact]
    public async Task Create_OrdinaryTextAndAnotherKindOfLink_Is201()
    {
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, _instructor.ProfileId);

        var (status, body) = await PostSessionAsync(courseId, "คาบที่ 1 — แนะนำคอร์ส", "สไลด์ที่ https://example.com/slides/week-1.pdf");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("สไลด์ที่ https://example.com/slides/week-1.pdf", body.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Update_MeetingLinkInTheTitleOrTheDescription_Is400WithTheStableReason()
    {
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, _instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, _instructor.Token, courseId);
        var start = DateTime.UtcNow.AddDays(3);

        using var withTitle = LiveIntegrationSupport.Authorized(HttpMethod.Put, Url(courseId, sessionId), _instructor.Token);
        withTitle.Content = JsonContent.Create(new { title = "https://zoom.us/j/111", description = (string?)null, startsAtUtc = start, endsAtUtc = start.AddHours(2) });
        var (titleStatus, titleBody) = await LiveIntegrationSupport.SendAsync(_client, withTitle);

        using var withDescription = LiveIntegrationSupport.Authorized(HttpMethod.Put, Url(courseId, sessionId), _instructor.Token);
        withDescription.Content = JsonContent.Create(new { title = "คาบที่ 1", description = "meet.google.com/abc-defg-hij", startsAtUtc = start, endsAtUtc = start.AddHours(2) });
        var (descriptionStatus, descriptionBody) = await LiveIntegrationSupport.SendAsync(_client, withDescription);

        AssertRejectedForMeetingLink(titleStatus, titleBody, "title");
        AssertRejectedForMeetingLink(descriptionStatus, descriptionBody, "description");
    }

    [Fact]
    public async Task Cancel_MeetingLinkInTheReason_Is400_AndTheSessionStaysScheduled()
    {
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, _instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, _instructor.Token, courseId);

        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Post, Url(courseId, sessionId, "/cancel"), _instructor.Token);
        request.Content = JsonContent.Create(new { reason = "ย้ายไปห้องใหม่ https://teams.live.com/meet/9876543210" });
        var (status, body) = await LiveIntegrationSupport.SendAsync(_client, request);

        AssertRejectedForMeetingLink(status, body, "reason");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(CourseLiveSessionStatus.Scheduled, (await db.CourseLiveSessions().AsNoTracking().SingleAsync(s => s.Id == sessionId)).Status);
    }

    // ---- Read side: a title stored before the rule ------------------------------------------------------------

    [Fact]
    public async Task PublicCourseDetail_ScrubsAMeetingLinkInAStoredTitle()
    {
        var (courseId, slug) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, _instructor.ProfileId);

        // Stored the way an older build (no validator rule) would have: straight through the domain method, then the course is published.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var course = await db.Courses().Include(c => c.LiveSessions).SingleAsync(c => c.Id == courseId);
            var start = clock.UtcNow.AddDays(2);
            var stored = course.AddLiveSession("ห้อง https://meet.google.com/abc-defg-hij นะ", "รายละเอียด", start, start.AddHours(2), clock);
            db.Entry(stored).State = EntityState.Added; // a new child with a preset key: tell EF it is an insert (CreateLiveSessionHandler does the same)
            course.Publish(clock);
            await db.SaveChangesAsync();
        }

        using var response = await _client.GetAsync($"/api/catalog/courses/{slug}"); // anonymous
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");

        using var json = JsonDocument.Parse(text);
        var session = json.RootElement.GetProperty("liveSchedule").GetProperty("sessions").EnumerateArray().Single();
        var title = session.GetProperty("title").GetString();
        Assert.Equal($"ห้อง {MeetingLinkText.Placeholder} นะ", title);
        Assert.DoesNotContain("meet.google.com", text);
        Assert.DoesNotContain("abc-defg-hij", text);
    }
}
