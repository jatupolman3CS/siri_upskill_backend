using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;
using Siri.SharedKernel;
using Xunit.Abstractions;

namespace Siri.IntegrationTests;

/// <summary>Skipped unless <c>SIRI_MEILISEARCH_LIVE_URL</c> and <c>SIRI_MEILISEARCH_LIVE_KEY</c> are set (same variables as the unit-level live smoke test).</summary>
public sealed class MeilisearchLiveApiFactAttribute : FactAttribute
{
    public const string UrlVariable = "SIRI_MEILISEARCH_LIVE_URL";
    public const string KeyVariable = "SIRI_MEILISEARCH_LIVE_KEY";

    public MeilisearchLiveApiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlVariable))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyVariable)))
        {
            Skip = $"Set {UrlVariable} and {KeyVariable} to run the API end to end against a real Meilisearch server.";
        }
    }
}

/// <summary>
/// The feature end to end, over real HTTP, on the real <c>Siri.Api</c> host (<see cref="SiriApiFactory"/> = <c>Program.cs</c> top to bottom, so the actual
/// configuration binding, DI, startup bootstrapper, authorization and controllers) with a REAL Meilisearch server and a throwaway LOCAL database: the admin status
/// endpoint, the admin reindex, a public search that can only succeed through Meilisearch (a typo no PostgreSQL substring match could find), and the unpublish hook.
/// <para>
/// Safe by construction: the database is the loopback-only throwaway one the fixture creates (the shared remote database is never touched, no Hangfire server runs), and the
/// Meilisearch index is a unique temporary uid — <b>not</b> the real <c>siriupskill_documents</c> — deleted at the end, so a reindex here can never remove documents of the
/// real index. The key is read from the environment and never printed.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class MeilisearchLiveApiTests(ContainersFixture containers, ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<(Guid CourseId, string Instructor)> SeedPublishedAsync(IServiceProvider services, string title, string instructor)
    {
        var clock = new SystemClock();
        var db = services.GetRequiredService<AppDbContext>();
        var category = CATEGORY.Create($"live-{Guid.NewGuid():N}", "หมวดทดสอบสด", "Live test", null, null, 0);
        var profile = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), instructor, "Live headline", "Live bio");
        profile.Approve(clock);
        var course = COURSE.Create($"live-{Guid.NewGuid():N}", title, profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 100m);
        var episode = course.AddEpisode(course.AddSection("Lessons").Id, "Lesson", null, false);
        course.AttachEpisodeMedia(episode.Id, Guid.NewGuid(), 12);
        course.SubmitForReview(clock);
        course.Publish(clock);
        db.Categories().Add(category);
        db.InstructorProfiles().Add(profile);
        db.Courses().Add(course);
        await db.SaveChangesAsync();
        return (course.Id, instructor);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static List<Guid> ResultIds(JsonElement search) =>
        search.GetProperty("results").GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

    [MeilisearchLiveApiFact]
    public async Task RealApiHost_RealMeilisearch_StatusReindexSearchByInstructorAndUnpublish()
    {
        var url = Environment.GetEnvironmentVariable(MeilisearchLiveApiFactAttribute.UrlVariable)!;
        var key = Environment.GetEnvironmentVariable(MeilisearchLiveApiFactAttribute.KeyVariable)!;
        var tempIndex = "siriupskill_e2e_" + Guid.NewGuid().ToString("N")[..10];
        output.WriteLine($"temporary index: {tempIndex}");

        await using var factory = new SiriApiFactory(containers, new Dictionary<string, string?>
        {
            ["Meilisearch:Url"] = url,
            ["Meilisearch:ApiKey"] = key,
            ["Meilisearch:DocumentsIndexUid"] = tempIndex,
        });

        using var meili = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/") };
        meili.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        try
        {
            // Booting the host registers + starts the real hosted services (CourseSearchIndexBootstrapper among them) with the real configuration binding.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            }

            Guid marketing;
            Guid investing;
            var admin = new TestUserBuilder().WithRole(ROLE.AdminName);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                await admin.BuildAsync(scope.ServiceProvider);
                marketing = (await SeedPublishedAsync(scope.ServiceProvider, "การตลาดออนไลน์ด้วย Facebook Ads", "ธนกฤต ศรีสุวรรณ")).CourseId;
                investing = (await SeedPublishedAsync(scope.ServiceProvider, "การลงทุนหุ้นและกองทุนรวม", "วิภา ลงทุนเก่ง")).CourseId;
            }

            var client = factory.CreateClient();
            var login = await ReadAsync(await client.PostAsJsonAsync(
                "/api/identity/login", new { email = admin.Email, password = admin.Password, deviceId = "live-e2e", deviceName = "Live e2e" }));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());

            // 1. The admin endpoint sees a configured, reachable engine (the index may or may not exist yet, depending on the bootstrapper's timing).
            var status = await ReadAsync(await client.GetAsync("/api/catalog/admin/search/status"));
            output.WriteLine($"status before reindex: enabled={status.GetProperty("enabled")} reachable={status.GetProperty("reachable")} indexed={status.GetProperty("indexedCourses")} published={status.GetProperty("publishedCoursesInDatabase")}");
            Assert.True(status.GetProperty("enabled").GetBoolean());
            Assert.True(status.GetProperty("reachable").GetBoolean(), status.TryGetProperty("message", out var statusMessage) ? statusMessage.ToString() : "reachable was false");
            Assert.Equal(tempIndex, status.GetProperty("indexUid").GetString());
            Assert.Equal(2, status.GetProperty("publishedCoursesInDatabase").GetInt32());

            // 2. Reindex from the database, then the index holds exactly what the database has Published.
            var reindex = await ReadAsync(await client.PostAsync("/api/catalog/admin/search/reindex", null));
            output.WriteLine($"reindex: {reindex}");
            Assert.True(reindex.GetProperty("executed").GetBoolean());
            Assert.Equal(2, reindex.GetProperty("indexed").GetInt32());

            status = await ReadAsync(await client.GetAsync("/api/catalog/admin/search/status"));
            output.WriteLine($"status after reindex: indexed={status.GetProperty("indexedCourses")} published={status.GetProperty("publishedCoursesInDatabase")}");
            Assert.Equal(2, status.GetProperty("indexedCourses").GetInt64());
            Assert.Equal(status.GetProperty("publishedCoursesInDatabase").GetInt32(), (int)status.GetProperty("indexedCourses").GetInt64());

            // 3. Public search by instructor name, and by a TYPO of it — PostgreSQL substring matching cannot find the typo, so a hit proves Meilisearch answered.
            var anonymous = factory.CreateClient();
            var exact = ResultIds(await ReadAsync(await anonymous.GetAsync("/api/catalog/courses/search?q=" + Uri.EscapeDataString("ธนกฤต"))));
            output.WriteLine($"search \"ธนกฤต\": {exact.Count} result(s)");
            Assert.Equal([marketing], exact);

            var typo = ResultIds(await ReadAsync(await anonymous.GetAsync("/api/catalog/courses/search?q=" + Uri.EscapeDataString("ธนกริต"))));
            output.WriteLine($"search typo \"ธนกริต\": {typo.Count} result(s)");
            Assert.Contains(marketing, typo);

            var byTitle = ResultIds(await ReadAsync(await anonymous.GetAsync("/api/catalog/courses/search?q=" + Uri.EscapeDataString("กองทุน"))));
            Assert.Contains(investing, byTitle);

            // 4. A fragment spanning two Thai words is something Meilisearch cannot match; the PostgreSQL safety net still does.
            var spanning = ResultIds(await ReadAsync(await anonymous.GetAsync("/api/catalog/courses/search?q=" + Uri.EscapeDataString("ตลาดออนไลน์"))));
            output.WriteLine($"search \"ตลาดออนไลน์\" (via database safety net): {spanning.Count} result(s)");
            Assert.Contains(marketing, spanning);

            // 5. Unpublishing syncs the index through the real handler hook; poll because Meilisearch applies the delete asynchronously.
            var unpublish = await client.PostAsJsonAsync($"/api/catalog/admin/courses/{investing}/unpublish", new { reason = "live e2e" });
            await ReadAsync(unpublish);
            long indexed = -1;
            for (var attempt = 0; attempt < 40 && indexed != 1; attempt++)
            {
                await Task.Delay(500);
                indexed = (await ReadAsync(await client.GetAsync("/api/catalog/admin/search/status"))).GetProperty("indexedCourses").GetInt64();
            }

            output.WriteLine($"indexed after unpublish: {indexed}");
            Assert.Equal(1, indexed);
            var afterUnpublish = ResultIds(await ReadAsync(await anonymous.GetAsync("/api/catalog/courses/search?q=" + Uri.EscapeDataString("กองทุน"))));
            Assert.DoesNotContain(investing, afterUnpublish);
        }
        finally
        {
            // Remove the temporary index (it only ever held this test's two synthetic courses).
            using var delete = await meili.DeleteAsync($"indexes/{tempIndex}");
            output.WriteLine($"temporary index delete: {(int)delete.StatusCode}");
            Assert.True(delete.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK or HttpStatusCode.NotFound);
        }
    }
}
