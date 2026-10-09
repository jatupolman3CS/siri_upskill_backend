using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

internal sealed record TestInstructor(Guid UserId, Guid ProfileId, string Token);

/// <summary>
/// Shared arrange helpers for the Live integration tests (P11-03): users with real roles + real logins, an approved instructor profile, a
/// Live course, and live sessions created through the real HTTP endpoint (so Catalog's handler calls the real <c>LiveMeetingSink</c>).
/// Everything goes through the production composition root; nothing is faked except the external services selected by configuration.
/// </summary>
internal static class LiveIntegrationSupport
{
    public static async Task<string> LoginAsync(HttpClient client, TestUserBuilder builder)
    {
        using var response = await client.PostAsJsonAsync("/api/identity/login", new
        {
            email = builder.Email,
            password = builder.Password,
            deviceId = $"live-test-{Guid.NewGuid():N}",
            deviceName = "Live Test Device",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        return body.AccessToken;
    }

    /// <summary>A user with the given system role, signed in over HTTP. Mind the "auth" rate limit (5 logins per minute per host).</summary>
    public static async Task<(USER User, string Token)> CreateUserAsync(WebApplicationFactory<Program> factory, HttpClient client, string roleName)
    {
        var builder = new TestUserBuilder().WithRole(roleName);

        await using var scope = factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);

        return (user, await LoginAsync(client, builder));
    }

    /// <summary>An instructor user with an APPROVED profile (the same state the real approval flow ends in), signed in.</summary>
    public static async Task<TestInstructor> CreateInstructorAsync(WebApplicationFactory<Program> factory, HttpClient client)
    {
        var builder = new TestUserBuilder().WithRole(ROLE.InstructorName);

        Guid userId;
        Guid profileId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var user = await builder.BuildAsync(scope.ServiceProvider);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Test Instructor", "Headline", "Bio");
            profile.Approve(clock);
            db.InstructorProfiles().Add(profile);
            await db.SaveChangesAsync();

            userId = user.Id;
            profileId = profile.Id;
        }

        return new TestInstructor(userId, profileId, await LoginAsync(client, builder));
    }

    public static async Task<(Guid CourseId, string Slug)> CreateLiveCourseAsync(
        WebApplicationFactory<Program> factory, Guid instructorProfileId, DeliveryFormat format = DeliveryFormat.Live)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test CATEGORY", null, null, 0);
        db.Categories().Add(category);

        var course = COURSE.Create(
            $"course-{Guid.NewGuid():N}",
            "คอร์สสดสำหรับทดสอบ",
            instructorProfileId,
            category.Id,
            CourseLevel.Beginner,
            CourseLanguage.Thai,
            1200m);
        course.SetDeliveryFormat(format);
        db.Courses().Add(course);
        await db.SaveChangesAsync();

        return (course.Id, course.Slug);
    }

    /// <summary>
    /// Retires courses the way the platform does (soft delete): their sessions then drop out of every schedule read. The invite reconcile job handles at most
    /// <c>SessionInviteService.MaxCoursesPerRun</c> courses per run and rotates which ones by the clock, so every test class that leaves live courses behind in the
    /// shared database makes a later class's single reconcile call less likely to reach its own course. Classes that create courses clean up after themselves.
    /// </summary>
    public static async Task RetireCoursesAsync(WebApplicationFactory<Program> factory, IEnumerable<Guid> courseIds)
    {
        var ids = courseIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses().RemoveRange(await db.Courses().Where(c => ids.Contains(c.Id)).ToListAsync());
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Retires every non-on-demand course already in the database - for a test class whose subject is a job that sweeps ALL courses (the invite reconcile) and
    /// therefore must not depend on how many live courses the classes that ran before it happened to leave behind (see <see cref="RetireCoursesAsync"/>).
    /// Safe in this collection: classes run one after another and every test builds its own world.
    /// </summary>
    public static async Task RetireAllLiveCoursesAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses().RemoveRange(await db.Courses().Where(c => c.DeliveryFormat != DeliveryFormat.OnDemand).ToListAsync());
        await db.SaveChangesAsync();
    }

    /// <summary>Creates a live session through the real endpoint (so the meeting row is staged by the real sink in the same SaveChanges).</summary>
    public static async Task<Guid> CreateSessionAsync(HttpClient client, string token, Guid courseId, int daysAhead = 2, int hourOffset = 0)
    {
        var start = DateTime.UtcNow.AddDays(daysAhead).AddHours(hourOffset);

        using var request = Authorized(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/live-sessions", token);
        request.Content = JsonContent.Create(new { title = "คาบทดสอบ", description = (string?)null, startsAtUtc = start, endsAtUtc = start.AddHours(2) });

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    public static HttpRequestMessage Authorized(HttpMethod method, string uri, string? token)
    {
        var request = new HttpRequestMessage(method, uri);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    public static async Task<(HttpStatusCode Status, JsonDocument Body)> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text));
    }

    public static HttpRequestMessage SetLinkRequest(string token, Guid sessionId, string? url)
    {
        var request = Authorized(HttpMethod.Put, $"/api/live/instructor/sessions/{sessionId}/meeting-link", token);
        request.Content = JsonContent.Create(new { meetUrl = url });
        return request;
    }

    public static async Task<string?> ReadRawColumnAsync(WebApplicationFactory<Program> factory, string column, Guid sessionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Raw SQL on purpose: proves what is physically stored, bypassing every EF/entity convention.
        return column switch
        {
            "MEET_URL_ENCRYPTED" => await db.Database
                .SqlQuery<string>($"SELECT \"MEET_URL_ENCRYPTED\" AS \"Value\" FROM \"LIVE\".\"SESSION_MEETINGS\" WHERE \"SESSION_ID\" = {sessionId}")
                .SingleOrDefaultAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };
    }

    /// <summary>Every property name and string value in the document, depth-first — for "this secret never appears anywhere in the response" assertions.</summary>
    public static IEnumerable<(string Path, string? Value)> Walk(JsonElement element, string path = "$")
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return ($"{path}.{property.Name}", null);
                    foreach (var child in Walk(property.Value, $"{path}.{property.Name}"))
                    {
                        yield return child;
                    }
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var child in Walk(item, $"{path}[{index++}]"))
                    {
                        yield return child;
                    }
                }

                break;
            case JsonValueKind.String:
                yield return (path, element.GetString());
                break;
        }
    }

    /// <summary>Asserts a response body carries no room URL: no URL-ish property, and none of the marker strings anywhere.</summary>
    public static void AssertNoRoomUrl(JsonDocument body, params string[] secretMarkers)
    {
        foreach (var (path, value) in Walk(body.RootElement))
        {
            var name = path[(path.LastIndexOf('.') + 1)..];
            Assert.DoesNotContain("meeturl", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("joinurl", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("roomurl", name, StringComparison.OrdinalIgnoreCase);

            if (value is not null)
            {
                foreach (var marker in secretMarkers)
                {
                    Assert.DoesNotContain(marker, value, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}
