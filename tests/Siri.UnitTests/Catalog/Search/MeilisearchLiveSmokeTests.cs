using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Infrastructure.Search;
using Siri.SharedKernel;
using Xunit.Abstractions;

namespace Siri.UnitTests.Catalog.Search;

/// <summary>Skipped unless <c>SIRI_MEILISEARCH_LIVE_URL</c> and <c>SIRI_MEILISEARCH_LIVE_KEY</c> are set — a manual check of
/// <see cref="MeilisearchCourseSearchIndex"/> against a REAL Meilisearch server (the stub-handler tests prove the requests match the documented API; this proves a
/// real engine accepts them and tokenises Thai the way the feature needs). Optional: <c>SIRI_MEILISEARCH_LIVE_INDEX</c> (default <c>siriupskill_documents</c>).</summary>
public sealed class MeilisearchLiveFactAttribute : FactAttribute
{
    public const string UrlVariable = "SIRI_MEILISEARCH_LIVE_URL";
    public const string KeyVariable = "SIRI_MEILISEARCH_LIVE_KEY";
    public const string IndexVariable = "SIRI_MEILISEARCH_LIVE_INDEX";

    public MeilisearchLiveFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlVariable))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyVariable)))
        {
            Skip = $"Set {UrlVariable} and {KeyVariable} (and optionally {IndexVariable}) to run against a real Meilisearch server.";
        }
    }
}

/// <summary>
/// The whole feature against a real server, using the production client: ensure index + settings, upsert Thai/English course documents carrying an instructor name,
/// search them (instructor name, Thai title words, English, typo, no-match), replace a document, list/count, delete. Every document uses a random id and a random
/// marker word, and the test removes everything it added, so it is safe to point at the real index. Never prints the key.
/// </summary>
public sealed class MeilisearchLiveSmokeTests(ITestOutputHelper output)
{
    private static string Env(string name, string fallback = "") =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static CourseSearchDocument Doc(
        Guid id, string title, string? description, string instructor, string? headline, string categoryTh, string categoryEn) =>
        CourseSearchDocument.ForCourse(id, "live-smoke-" + id.ToString("N")[..8], title, null, description, Guid.NewGuid(), instructor, headline, Guid.NewGuid(), categoryTh, categoryEn);

    private async Task<IReadOnlyList<Guid>> SearchAsync(MeilisearchCourseSearchIndex index, string query, Guid a, Guid b, Guid c)
    {
        var ids = await index.SearchCourseIdsAsync(query, CancellationToken.None);
        Assert.NotNull(ids);
        var ours = ids.Select(id => id == a ? "A" : id == b ? "B" : id == c ? "C" : null).Where(label => label is not null);
        output.WriteLine($"  search \"{query}\" -> {ids.Count} hit(s); ours in order: [{string.Join(",", ours)}]");
        return ids;
    }

    [MeilisearchLiveFact]
    public async Task RealServer_IndexesAndFindsCoursesByThaiTitleAndInstructorName_ThenCleansUp()
    {
        var uid = Env(MeilisearchLiveFactAttribute.IndexVariable, "siriupskill_documents");
        var options = new MeilisearchOptions
        {
            Url = Env(MeilisearchLiveFactAttribute.UrlVariable),
            ApiKey = Env(MeilisearchLiveFactAttribute.KeyVariable),
            DocumentsIndexUid = uid,
            SearchTimeoutMilliseconds = 10_000,
            TaskWaitTimeoutSeconds = 90,
        };
        Assert.True(options.IsActive, "the live URL/key variables must describe an active configuration");

        var wrapped = Options.Create(options);
        var availability = new MeilisearchAvailability(new SystemClock(), wrapped);
        using var http = new HttpClient { BaseAddress = options.GetBaseAddress(), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());
        var index = new MeilisearchCourseSearchIndex(http, wrapped, availability, NullLogger<MeilisearchCourseSearchIndex>.Instance);

        var marker = "zq" + Guid.NewGuid().ToString("N")[..8];
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var documents = new[]
        {
            Doc(a, "การตลาดออนไลน์ด้วย Facebook Ads", "เรียนรู้การทำโฆษณาและวางแผนแคมเปญ", $"ธนกฤต ศรีสุวรรณ {marker}", "ผู้เชี่ยวชาญการตลาดดิจิทัล", "การตลาดและธุรกิจ", "Marketing"),
            Doc(b, "Python เบื้องต้นสำหรับมือใหม่", "เริ่มเขียนโปรแกรมตั้งแต่ศูนย์", $"สมชาย ใจดี {marker}", "Senior Developer", "โปรแกรมมิ่ง", "Programming"),
            Doc(c, "การลงทุนหุ้นและกองทุนรวม", "วางแผนการเงินส่วนบุคคล", $"วิภา ลงทุนเก่ง {marker}", null, "การเงินและการลงทุน", "Finance"),
        };

        output.WriteLine($"index={uid}  marker={marker}");
        try
        {
            // 1. index + settings (twice: must be idempotent)
            await index.EnsureIndexAsync(CancellationToken.None);
            await index.EnsureIndexAsync(CancellationToken.None);
            output.WriteLine("ensure index+settings: ok (x2)");

            // 2. write and wait for the engine to finish
            await index.UpsertCoursesAsync(documents, waitForCompletion: true, CancellationToken.None);
            output.WriteLine("upsert 3 documents: ok");

            // 3. bookkeeping reads
            var count = await index.CountCourseDocumentsAsync(CancellationToken.None);
            var listed = await index.ListIndexedCourseIdsAsync(CancellationToken.None);
            output.WriteLine($"course documents in index: {count}; listed: {listed.Count}");
            Assert.True(count >= 3);
            Assert.Contains(a, listed);
            Assert.Contains(b, listed);
            Assert.Contains(c, listed);

            // 4. search — the unique marker (ASCII) proves each document is searchable at all
            var byMarker = await SearchAsync(index, marker, a, b, c);
            Assert.Equal(new HashSet<Guid> { a, b, c }, byMarker.ToHashSet());

            // 5. search by the instructor's Thai name (the point of the feature)
            var byFirstName = await SearchAsync(index, "ธนกฤต", a, b, c);
            Assert.Contains(a, byFirstName);
            Assert.DoesNotContain(b, byFirstName);
            Assert.DoesNotContain(c, byFirstName);

            var byFullName = await SearchAsync(index, $"ธนกฤต ศรีสุวรรณ {marker}", a, b, c);
            Assert.Equal(a, byFullName[0]);

            var byLastName = await SearchAsync(index, "วิภา", a, b, c);
            Assert.Contains(c, byLastName);

            // 6. Thai title words (no spaces in Thai: depends on Meilisearch's segmenter)
            Assert.Contains(a, await SearchAsync(index, "การตลาด", a, b, c));
            Assert.Contains(c, await SearchAsync(index, "หุ้น", a, b, c));
            Assert.Contains(c, await SearchAsync(index, "กองทุน", a, b, c));

            // 7. English, and prefix-as-you-type
            Assert.Contains(b, await SearchAsync(index, "python", a, b, c));
            Assert.Contains(b, await SearchAsync(index, "pyth", a, b, c));

            // 8. things worth knowing but not required for the feature to work — reported, not asserted
            output.WriteLine("-- informational --");
            await SearchAsync(index, "ออนไลน์", a, b, c);
            await SearchAsync(index, "ตลาดออนไลน์", a, b, c);
            await SearchAsync(index, "pyhton", a, b, c);
            await SearchAsync(index, "ธนกริต", a, b, c);
            await SearchAsync(index, "marketing", a, b, c);
            await SearchAsync(index, "การเงิน", a, b, c);

            // 9. nothing matches nonsense
            var nonsense = await SearchAsync(index, "qqxzvv" + marker + "nomatch", a, b, c);
            Assert.DoesNotContain(a, nonsense);
            Assert.DoesNotContain(b, nonsense);
            Assert.DoesNotContain(c, nonsense);

            // 10. replacing a document by id (what an approve/reindex does)
            var replacement = Doc(a, $"ชื่อใหม่ของคอร์ส {marker}upd", null, $"ครูใหม่ {marker}", null, "การตลาดและธุรกิจ", "Marketing");
            await index.UpsertCoursesAsync([replacement], waitForCompletion: true, CancellationToken.None);
            Assert.Contains(a, await SearchAsync(index, marker + "upd", a, b, c));
            Assert.DoesNotContain(a, await SearchAsync(index, "ธนกฤต " + marker, a, b, c));
        }
        finally
        {
            // 11. remove everything this test added
            await index.DeleteCoursesAsync([a, b, c], CancellationToken.None);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            IReadOnlyCollection<Guid> remaining;
            do
            {
                await Task.Delay(500);
                remaining = (await index.ListIndexedCourseIdsAsync(CancellationToken.None)).Where(id => id == a || id == b || id == c).ToList();
            }
            while (remaining.Count > 0 && DateTime.UtcNow < deadline);

            output.WriteLine($"cleanup: {(remaining.Count == 0 ? "all test documents removed" : "STILL PRESENT: " + remaining.Count)}");
            Assert.Empty(remaining);
        }
    }
}
