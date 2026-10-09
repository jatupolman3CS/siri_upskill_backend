using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.SearchCourses;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Catalog.Infrastructure.Search;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// The Meilisearch-backed course search against a real PostgreSQL (Testcontainers; needs Docker like every integration test here). The search engine is
/// replaced by <see cref="RecordingCourseSearchIndex"/>, so these prove OUR logic — which courses a set of engine hits turns into, when the database
/// fallback runs, what the indexer sends — while the engine's own HTTP contract is covered by the unit tests in <c>MeilisearchCourseSearchIndexTests</c>.
/// Test data uses a unique token per test because every test in this class shares one database.
/// </summary>
public sealed class CourseSearchMeilisearchIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class Anonymous : IUserContext
    {
        public Guid? UserId => null;

        public IReadOnlyCollection<string> Roles => [];

        public bool IsAuthenticated => false;
    }

    private sealed record Seeded(Guid CourseId, Guid InstructorId, string Token);

    private static string NewToken() => "zq" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Inserts a category, an approved instructor and a course. <paramref name="published"/> false leaves the course a Draft.</summary>
    private async Task<Seeded> SeedCourseAsync(string title, string instructorName, bool published = true)
    {
        var clock = new SystemClock();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = CATEGORY.Create($"search-{Guid.NewGuid():N}", "หมวดทดสอบ", "Search test", null, null, 0);
        var instructor = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), instructorName, "Test headline", "Test bio");
        instructor.Approve(clock);
        var course = COURSE.Create($"search-{Guid.NewGuid():N}", title, instructor.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 100m);
        var section = course.AddSection("Lessons");
        var episode = course.AddEpisode(section.Id, "Lesson", null, false);
        if (published)
        {
            course.AttachEpisodeMedia(episode.Id, Guid.NewGuid(), 12);
            course.SubmitForReview(clock);
            course.Publish(clock);
        }

        db.Categories().Add(category);
        db.InstructorProfiles().Add(instructor);
        db.Courses().Add(course);
        await db.SaveChangesAsync();

        return new Seeded(course.Id, instructor.Id, instructorName);
    }

    private async Task<SearchCoursesResponse> SearchAsync(string? text, RecordingCourseSearchIndex index)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new SearchCoursesHandler(db, new Anonymous(), new SystemClock(), index);

        return await handler.HandleAsync(
            new SearchCoursesQuery(text, null, null, null, null, null, null, CourseSearchSort.Relevance, 1, 50),
            CancellationToken.None);
    }

    private CourseSearchIndexer NewIndexer(AppDbContext db, RecordingCourseSearchIndex index) =>
        new(db, index, Options.Create(new MeilisearchOptions()), NullLogger<CourseSearchIndexer>.Instance);

    // ---- search handler -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Search_EngineHits_AreKeptInEngineOrder_AndOnlyPublishedCoursesSurvive()
    {
        var a = await SeedCourseAsync("Alpha course", "Teacher A");
        var b = await SeedCourseAsync("Beta course", "Teacher B");
        var c = await SeedCourseAsync("Gamma course", "Teacher C");
        var draft = await SeedCourseAsync("Draft course", "Teacher D", published: false);
        var index = new RecordingCourseSearchIndex
        {
            // Engine order: draft first (a stale entry), then C, then A. B is not a hit. An unknown id is ignored.
            SearchResult = [draft.CourseId, c.CourseId, Guid.NewGuid(), a.CourseId],
        };

        // "anything" matches no title: the courses can only be found because the ENGINE named them.
        var response = await SearchAsync("anything", index);

        Assert.Equal([c.CourseId, a.CourseId], response.Results.Items.Select(i => i.Id));
        Assert.DoesNotContain(response.Results.Items, i => i.Id == b.CourseId);
        Assert.DoesNotContain(response.Results.Items, i => i.Id == draft.CourseId);
        Assert.Equal(["anything"], index.SearchedTexts);
    }

    [Fact]
    public async Task Search_EngineHit_CarriesTheInstructorNameIntoTheResult()
    {
        var course = await SeedCourseAsync("Some title", "ธนกฤต ศรีสุวรรณ");
        var index = new RecordingCourseSearchIndex { SearchResult = [course.CourseId] };

        var response = await SearchAsync("ธนกฤต", index);

        var item = Assert.Single(response.Results.Items);
        Assert.Equal("ธนกฤต ศรีสุวรรณ", item.InstructorDisplayName);
        Assert.Contains(response.Facets.Instructors, f => f.InstructorId == course.InstructorId && f.DisplayName == "ธนกฤต ศรีสุวรรณ");
    }

    [Fact]
    public async Task Search_EngineUnavailable_FallsBackToTheDatabaseTitleSearch()
    {
        var token = NewToken();
        var course = await SeedCourseAsync($"Intro to {token} programming", "Fallback Teacher");
        await SeedCourseAsync("Unrelated course", "Other Teacher");
        var index = new RecordingCourseSearchIndex { SearchResult = null };

        var response = await SearchAsync(token, index);

        Assert.Equal([course.CourseId], response.Results.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Search_EngineFindsNothing_StillTriesTheDatabaseSoWordFragmentsAreNotLost()
    {
        var token = NewToken();
        var course = await SeedCourseAsync($"Master{token}Class", "Fragment Teacher");
        var index = new RecordingCourseSearchIndex { SearchResult = [] };

        // A fragment from the MIDDLE of a word: prefix-based engines cannot match it, pg_trgm's ILIKE can.
        var response = await SearchAsync(token, index);

        Assert.Contains(response.Results.Items, i => i.Id == course.CourseId);
    }

    [Fact]
    public async Task Search_ByInstructorName_WhenEngineIsDown_FindsThatInstructorsCourses()
    {
        var token = NewToken();
        var teacherName = $"ครู{token} ทดสอบ";
        var first = await SeedCourseAsync("Course one with no matching words", teacherName);
        var other = await SeedCourseAsync("Course two with no matching words", "Someone Else");
        var index = new RecordingCourseSearchIndex { SearchResult = null };

        var response = await SearchAsync(token.ToUpperInvariant(), index);

        Assert.Contains(response.Results.Items, i => i.Id == first.CourseId);
        Assert.DoesNotContain(response.Results.Items, i => i.Id == other.CourseId);
    }

    [Fact]
    public async Task Search_WithoutText_NeverAsksTheEngine()
    {
        await SeedCourseAsync("Browse me", "Browse Teacher");
        var index = new RecordingCourseSearchIndex { SearchResult = [] };

        var response = await SearchAsync(null, index);

        Assert.NotEmpty(response.Results.Items);
        Assert.Empty(index.SearchedTexts);
    }

    // ---- indexer --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SyncCourse_PublishedCourse_IsUpsertedWithItsInstructorAndCategoryNames()
    {
        var course = await SeedCourseAsync("Indexed title", "Indexed Teacher");
        var index = new RecordingCourseSearchIndex();

        using var scope = fixture.CreateScope();
        await NewIndexer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), index).SyncCourseAsync(course.CourseId, CancellationToken.None);

        var document = Assert.Single(index.Upserted);
        Assert.Equal(CourseSearchDocument.IdFor(course.CourseId), document.Id);
        Assert.Equal("Indexed title", document.Title);
        Assert.Equal("Indexed Teacher", document.InstructorName);
        Assert.Equal("Test headline", document.InstructorHeadline);
        Assert.Equal("หมวดทดสอบ", document.CategoryNameTh);
        Assert.Equal("Search test", document.CategoryNameEn);
        Assert.Empty(index.Deleted);
    }

    [Fact]
    public async Task SyncCourse_CourseThatIsNotPublished_IsRemovedFromTheIndex()
    {
        var draft = await SeedCourseAsync("Not public", "Teacher", published: false);
        var index = new RecordingCourseSearchIndex();

        using var scope = fixture.CreateScope();
        await NewIndexer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), index).SyncCourseAsync(draft.CourseId, CancellationToken.None);

        Assert.Equal([draft.CourseId], index.Deleted);
        Assert.Empty(index.Upserted);
    }

    [Fact]
    public async Task SyncCourse_EngineOutage_IsSwallowedSoAnApprovalIsNeverFailedBySearch()
    {
        var course = await SeedCourseAsync("Outage title", "Teacher");
        var index = new RecordingCourseSearchIndex { FailWrites = true };

        using var scope = fixture.CreateScope();
        await NewIndexer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), index).SyncCourseAsync(course.CourseId, CancellationToken.None);
    }

    [Fact]
    public async Task ReindexAll_WritesEveryPublishedCourse_AndDeletesOnlyWhatIsNoLongerPublished()
    {
        var published = await SeedCourseAsync("Reindex published", "Teacher");
        var draft = await SeedCourseAsync("Reindex draft", "Teacher", published: false);
        var staleGhost = Guid.NewGuid();
        var index = new RecordingCourseSearchIndex();
        index.IndexedIds.AddRange([published.CourseId, draft.CourseId, staleGhost]);

        CourseSearchReindexResult result;
        using (var scope = fixture.CreateScope())
        {
            result = await NewIndexer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), index).ReindexAllAsync(CancellationToken.None);
        }

        Assert.True(result.Executed);
        Assert.Contains(index.Upserted, d => d.CourseId == published.CourseId);
        Assert.DoesNotContain(index.Upserted, d => d.CourseId == draft.CourseId);
        Assert.Contains(draft.CourseId, index.Deleted);
        Assert.Contains(staleGhost, index.Deleted);
        Assert.DoesNotContain(published.CourseId, index.Deleted);
        Assert.Equal(1, index.EnsureCalls);
        Assert.Equal(result.Indexed, index.Upserted.Count);
    }

    [Fact]
    public async Task ReindexAll_CourseApprovedWhileTheRunWasInFlight_IsNotDeletedAsStale()
    {
        // The index lists a course the run's own snapshot did not contain (approved a moment ago and synced by the approve hook). It is Published in the
        // database, so the re-check before deleting must keep it.
        var published = await SeedCourseAsync("Approved mid-run", "Teacher");

        using var scope = fixture.CreateScope();
        var indexer = NewIndexer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), new RecordingCourseSearchIndex());

        var removable = await indexer.FilterStillUnpublishedAsync([published.CourseId, Guid.NewGuid()], CancellationToken.None);

        Assert.DoesNotContain(published.CourseId, removable);
        Assert.Single(removable);
    }

    [Fact]
    public async Task Bootstrap_EmptyIndex_IsFilled_ButAPopulatedIndexIsLeftAlone()
    {
        var course = await SeedCourseAsync("Bootstrap title", "Teacher");

        var empty = new RecordingCourseSearchIndex { CourseCount = 0 };
        using (var scope = fixture.CreateScope())
        {
            await NewIndexer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), empty).BootstrapAsync(CancellationToken.None);
        }

        Assert.Contains(empty.Upserted, d => d.CourseId == course.CourseId);

        var populated = new RecordingCourseSearchIndex { CourseCount = 12 };
        using (var scope = fixture.CreateScope())
        {
            await NewIndexer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), populated).BootstrapAsync(CancellationToken.None);
        }

        Assert.Empty(populated.Upserted);
        Assert.Equal(1, populated.EnsureCalls);
    }

    [Fact]
    public async Task Status_ReportsTheIndexedCountNextToThePublishedCountInTheDatabase()
    {
        await SeedCourseAsync("Status title", "Teacher");
        var index = new RecordingCourseSearchIndex { CourseCount = 3 };

        CourseSearchStatus status;
        using (var scope = fixture.CreateScope())
        {
            status = await NewIndexer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), index).GetStatusAsync(CancellationToken.None);
        }

        Assert.True(status.Enabled);
        Assert.True(status.Reachable);
        Assert.Equal(3, status.IndexedCourses);
        Assert.True(status.PublishedCoursesInDatabase >= 1);
        Assert.Equal("siriupskill_documents", status.IndexUid);
    }
}
