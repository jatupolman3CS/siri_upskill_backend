using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;

namespace Siri.IntegrationTests;

/// <summary>
/// <c>PUT /api/catalog/instructor/courses/{courseId}/live-settings</c> (P11-04 §6.1) over real HTTP against the production composition root: the
/// per-course opt-in to Google Calendar attendee sync. Ownership follows the Catalog pattern (404 not found / 403 not the owner, kept apart),
/// an omitted field is a 400 rather than a silent "turn it off", and an archived course is a 409.
/// <para>Requires Docker like every test in this collection.</para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CourseLiveSettingsIntegrationTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public CourseLiveSettingsIntegrationTests(ContainersFixture containers)
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

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static HttpRequestMessage Put(Guid courseId, string? token, object body)
    {
        var request = LiveIntegrationSupport.Authorized(HttpMethod.Put, $"/api/catalog/instructor/courses/{courseId}/live-settings", token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<bool> StoredFlagAsync(Guid courseId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var course = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Courses().AsNoTracking().SingleAsync(c => c.Id == courseId);
        return course.GoogleAttendeeSyncEnabled;
    }

    [Fact]
    public async Task NewCourse_StartsWithTheSyncOff()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);

        Assert.False(await StoredFlagAsync(courseId));
    }

    [Fact]
    public async Task Owner_CanTurnTheSyncOnAndOffAgain()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);

        var (onStatus, onBody) = await LiveIntegrationSupport.SendAsync(_client, Put(courseId, instructor.Token, new { googleAttendeeSyncEnabled = true }));

        Assert.Equal(HttpStatusCode.OK, onStatus);
        Assert.Equal(courseId, onBody.RootElement.GetProperty("courseId").GetGuid());
        Assert.True(onBody.RootElement.GetProperty("googleAttendeeSyncEnabled").GetBoolean());
        Assert.True(await StoredFlagAsync(courseId));

        var (offStatus, offBody) = await LiveIntegrationSupport.SendAsync(_client, Put(courseId, instructor.Token, new { googleAttendeeSyncEnabled = false }));

        Assert.Equal(HttpStatusCode.OK, offStatus);
        Assert.False(offBody.RootElement.GetProperty("googleAttendeeSyncEnabled").GetBoolean());
        Assert.False(await StoredFlagAsync(courseId));
    }

    [Fact]
    public async Task AnOmittedFlag_Is400_AndChangesNothing()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        await LiveIntegrationSupport.SendAsync(_client, Put(courseId, instructor.Token, new { googleAttendeeSyncEnabled = true }));

        var (status, _) = await LiveIntegrationSupport.SendAsync(_client, Put(courseId, instructor.Token, new { }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(await StoredFlagAsync(courseId)); // an empty body is not "off"
    }

    [Fact]
    public async Task AnotherInstructor_Gets403_AndTheFlagIsUntouched()
    {
        var owner = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var other = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, owner.ProfileId);

        var (status, _) = await LiveIntegrationSupport.SendAsync(_client, Put(courseId, other.Token, new { googleAttendeeSyncEnabled = true }));

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.False(await StoredFlagAsync(courseId));
    }

    [Fact]
    public async Task AnUnknownCourse_Is404()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);

        var (status, _) = await LiveIntegrationSupport.SendAsync(_client, Put(Guid.NewGuid(), instructor.Token, new { googleAttendeeSyncEnabled = true }));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task AnArchivedCourse_Is409()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await db.Courses().SingleAsync(c => c.Id == courseId);
            course.Archive();
            await db.SaveChangesAsync();
        }

        var (status, _) = await LiveIntegrationSupport.SendAsync(_client, Put(courseId, instructor.Token, new { googleAttendeeSyncEnabled = true }));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(await StoredFlagAsync(courseId));
    }

    [Fact]
    public async Task Anonymous_Is401_AndALearner_Is403()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var (_, learnerToken) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.LearnerName);

        var (anonymous, _) = await LiveIntegrationSupport.SendAsync(_client, Put(courseId, null, new { googleAttendeeSyncEnabled = true }));
        var (learner, _) = await LiveIntegrationSupport.SendAsync(_client, Put(courseId, learnerToken, new { googleAttendeeSyncEnabled = true }));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous);
        Assert.Equal(HttpStatusCode.Forbidden, learner);
        Assert.False(await StoredFlagAsync(courseId));
    }
}
