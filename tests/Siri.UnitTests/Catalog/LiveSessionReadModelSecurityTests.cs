using System.Reflection;
using System.Text.Json;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.GetCourseDetail;
using Siri.Modules.Catalog.Features.SearchCourses;
using Siri.SharedKernel;

namespace Siri.UnitTests.Catalog;

/// <summary>
/// Security requirement verification for P11-07 and docs/contracts/P11-01-catalog-live-sessions.md §5:
/// "response body (deserialize เป็น raw JsonDocument, เดินทุก property name แบบ recursive)
///  ต้องไม่มี property ชื่อที่มีคำว่า meetUrl/meetingUrl/meet_url ที่ไหนเลยในทุก response"
/// </summary>
public class LiveSessionReadModelSecurityTests
{
    private static readonly string[] ForbiddenSubstrings = ["meeturl", "meetingurl", "meet_url"];

    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData(typeof(CourseDetailResponse))]
    [InlineData(typeof(CourseDetailLiveSchedule))]
    [InlineData(typeof(CourseDetailLiveSession))]
    [InlineData(typeof(CourseSearchResultItem))]
    [InlineData(typeof(CourseSearchFacets))]
    [InlineData(typeof(FormatFacet))]
    [InlineData(typeof(SearchCoursesResponse))]
    public void ReadModelTypes_DoNotDeclareAnyMeetingUrlProperties(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var lowerName = prop.Name.ToLowerInvariant();
            foreach (var forbidden in ForbiddenSubstrings)
            {
                Assert.False(
                    lowerName.Contains(forbidden),
                    $"Type '{type.Name}' exposes forbidden property '{prop.Name}' matching '{forbidden}'.");
            }
        }
    }

    [Fact]
    public void CourseDetailResponse_SerializedJsonAst_ContainsZeroMeetingUrlProperties()
    {
        var courseId = Guid.NewGuid();
        var instructor = new CourseDetailInstructor(Guid.NewGuid(), "Somchai Dev", "Lead Engineer", null);
        var episode = new CourseDetailEpisode(Guid.NewGuid(), "Intro Ep", 0, 300, true);
        var section = new CourseDetailSection(Guid.NewGuid(), "Section 1", 0, [episode]);

        var liveSession1 = new CourseDetailLiveSession(
            Guid.NewGuid(),
            "Live Session 1",
            DateTime.UtcNow.AddHours(2),
            DateTime.UtcNow.AddHours(4),
            LiveSessionDisplayState.Upcoming,
            HasRecording: false);

        var liveSession2 = new CourseDetailLiveSession(
            Guid.NewGuid(),
            "Live Session 2",
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(1).AddHours(2),
            LiveSessionDisplayState.Upcoming,
            HasRecording: true);

        var schedule = new CourseDetailLiveSchedule(
            "Asia/Bangkok",
            UpcomingCount: 2,
            PastCount: 0,
            NextStartsAtUtc: liveSession1.StartsAtUtc,
            Sessions: [liveSession1, liveSession2]);

        var response = new CourseDetailResponse(
            courseId,
            "mastering-aspnet-core",
            "Mastering ASP.NET Core",
            "From Zero to Hero",
            "Complete course description",
            CourseLevel.Intermediate,
            CourseLanguage.Thai,
            "https://cdn.example.com/thumb.jpg",
            1990m,
            2500m,
            "THB",
            365,
            4.9m,
            120,
            450,
            1,
            300,
            "SEO Title",
            "SEO Description",
            DateTime.UtcNow.AddDays(-10),
            Guid.NewGuid(),
            instructor,
            ["Outcome 1", "Outcome 2"],
            ["Requirement 1"],
            [section],
            IsWishlisted: true,
            DeliveryFormat: DeliveryFormat.Hybrid,
            LiveSchedule: schedule);

        var json = JsonSerializer.Serialize(response, WebJsonOptions);
        using var doc = JsonDocument.Parse(json);

        var forbiddenHits = FindForbiddenPropertyHits(doc.RootElement);
        Assert.Empty(forbiddenHits);
    }

    [Fact]
    public void SearchCoursesResponse_SerializedJsonAst_ContainsZeroMeetingUrlProperties()
    {
        var instructorId = Guid.NewGuid();
        var item1 = new CourseSearchResultItem(
            Guid.NewGuid(),
            "dotnet-core-mastery",
            ".NET Core Mastery",
            "Subtitle 1",
            null,
            990m,
            null,
            "THB",
            CourseLevel.Beginner,
            CourseLanguage.Thai,
            4.8m,
            50,
            200,
            10,
            7200,
            instructorId,
            "Instructor One",
            Guid.NewGuid(),
            IsWishlisted: false,
            DeliveryFormat: DeliveryFormat.Live,
            NextStartsAtUtc: DateTime.UtcNow.AddDays(2));

        var item2 = new CourseSearchResultItem(
            Guid.NewGuid(),
            "csharp-basics",
            "C# Basics",
            "Subtitle 2",
            null,
            490m,
            null,
            "THB",
            CourseLevel.Beginner,
            CourseLanguage.Thai,
            4.5m,
            20,
            80,
            5,
            3600,
            instructorId,
            "Instructor One",
            Guid.NewGuid(),
            IsWishlisted: true,
            DeliveryFormat: DeliveryFormat.OnDemand,
            NextStartsAtUtc: null);

        var pagedResult = PagedResult<CourseSearchResultItem>.Create([item1, item2], 2, 1, 20);
        var facets = new CourseSearchFacets(
            Categories: [new CategoryFacet(Guid.NewGuid(), 2)],
            Levels: [new LevelFacet(CourseLevel.Beginner, 2)],
            Instructors: [new InstructorFacet(instructorId, "Instructor One", 2)],
            Formats: [
                new FormatFacet(DeliveryFormat.Live, 1),
                new FormatFacet(DeliveryFormat.OnDemand, 1),
                new FormatFacet(DeliveryFormat.Hybrid, 0)
            ]);

        var response = new SearchCoursesResponse(pagedResult, facets);

        var json = JsonSerializer.Serialize(response, WebJsonOptions);
        using var doc = JsonDocument.Parse(json);

        var forbiddenHits = FindForbiddenPropertyHits(doc.RootElement);
        Assert.Empty(forbiddenHits);
    }

    private static List<string> FindForbiddenPropertyHits(JsonElement element, string currentPath = "$")
    {
        var hits = new List<string>();

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var propPath = $"{currentPath}.{prop.Name}";
                    var lowerName = prop.Name.ToLowerInvariant();

                    foreach (var forbidden in ForbiddenSubstrings)
                    {
                        if (lowerName.Contains(forbidden))
                        {
                            hits.Add($"Property '{propPath}' matches forbidden pattern '{forbidden}'");
                        }
                    }

                    hits.AddRange(FindForbiddenPropertyHits(prop.Value, propPath));
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    hits.AddRange(FindForbiddenPropertyHits(item, $"{currentPath}[{index}]"));
                    index++;
                }
                break;
        }

        return hits;
    }
}
