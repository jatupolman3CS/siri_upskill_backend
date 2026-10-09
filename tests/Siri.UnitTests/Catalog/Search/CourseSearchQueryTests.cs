using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Features.SearchCourses;
using Siri.Modules.Catalog.Infrastructure.Search;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.UnitTests.Catalog.Search;

/// <summary>
/// The pieces of the Meilisearch-backed course search that can be proven without a database: the rank ordering, that every LINQ query the new code
/// issues translates to SQL (EF translates before it opens a connection, so against an unreachable server a translatable query ends at the
/// connection-opening interceptor while an untranslatable one fails with an <see cref="InvalidOperationException"/> — the technique
/// <c>InstructorStatsReadersTests</c> established), the document mapping, and that nothing touches the database while Meilisearch is off.
/// What the queries return is covered by <c>CourseSearchMeilisearchIntegrationTests</c> (Testcontainers).
/// </summary>
public sealed class CourseSearchQueryTests
{
    private sealed class ReachedDatabaseException : Exception;

    private sealed class StopAtConnectionOpen : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw new ReachedDatabaseException();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new ReachedDatabaseException();
    }

    private sealed class Anonymous : IUserContext
    {
        public Guid? UserId => null;

        public IReadOnlyCollection<string> Roles => [];

        public bool IsAuthenticated => false;
    }

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unit-test;Username=none;Password=none;Pooling=false")
            .AddInterceptors(new StopAtConnectionOpen())
            .Options);

    private static CourseSearchIndexer Indexer(AppDbContext context, ICourseSearchIndex? index = null) =>
        new(context, index ?? new DisabledCourseSearchIndex(), Options.Create(new MeilisearchOptions()), NullLogger<CourseSearchIndexer>.Instance);

    private static async Task AssertTranslatesAsync(Func<AppDbContext, Task> query)
    {
        using var context = Context();

        var exception = await Record.ExceptionAsync(() => query(context));

        Assert.NotNull(exception);
        Assert.True(Chain(exception).Any(e => e is ReachedDatabaseException), exception.ToString());
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    // ---- ranking --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void RankByPosition_FirstHitIsTheMostRelevant()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var ranks = SearchCoursesHandler.RankByPosition([a, b, c]);

        Assert.Equal(3, ranks[a]);
        Assert.Equal(2, ranks[b]);
        Assert.Equal(1, ranks[c]);
        Assert.Equal([a, b, c], ranks.OrderByDescending(pair => pair.Value).Select(pair => pair.Key));
    }

    [Fact]
    public void RankByPosition_DuplicateId_KeepsItsBestPosition()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var ranks = SearchCoursesHandler.RankByPosition([a, b, a]);

        Assert.Equal(2, ranks.Count);
        Assert.Equal(3, ranks[a]);
    }

    [Fact]
    public void RankByPosition_Empty_IsEmpty() => Assert.Empty(SearchCoursesHandler.RankByPosition([]));

    // ---- queries translate ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public Task SearchHandler_InstructorNameFallbackQuery_Translates() =>
        AssertTranslatesAsync(context =>
            new SearchCoursesHandler(context, new Anonymous(), new FakeClock(DateTime.UtcNow), new DisabledCourseSearchIndex())
                .InstructorNameMatchQuery("สมชาย").ToListAsync());

    [Fact]
    public Task Indexer_PublishedRowsQuery_AllCourses_Translates() =>
        AssertTranslatesAsync(context => Indexer(context).PublishedRows(null).Skip(500).Take(500).ToListAsync());

    [Fact]
    public Task Indexer_PublishedRowsQuery_OneCourse_Translates() =>
        AssertTranslatesAsync(context => Indexer(context).PublishedRows(Guid.NewGuid()).FirstOrDefaultAsync());

    [Fact]
    public Task Indexer_StaleCandidateRecheckQuery_Translates() =>
        AssertTranslatesAsync(context => Indexer(context).FilterStillUnpublishedAsync([Guid.NewGuid(), Guid.NewGuid()], CancellationToken.None));

    [Fact]
    public async Task Indexer_StaleCandidateRecheck_NoCandidates_DoesNotTouchTheDatabase()
    {
        using var context = Context();

        var removable = await Indexer(context).FilterStillUnpublishedAsync([], CancellationToken.None);

        Assert.Empty(removable);
    }

    // ---- mapping --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ToDocument_MapsEveryRowFieldIntoTheIndexDocument()
    {
        var row = new CourseSearchIndexer.CourseIndexRow(
            Guid.NewGuid(), "slug", "Title", "Sub", "<p>Desc</p>", Guid.NewGuid(), "Teacher", "Head", Guid.NewGuid(), "หมวด", "Category");

        var document = CourseSearchIndexer.ToDocument(row);

        Assert.Equal(CourseSearchDocument.IdFor(row.CourseId), document.Id);
        Assert.Equal(row.Slug, document.Slug);
        Assert.Equal(row.Title, document.Title);
        Assert.Equal(row.Subtitle, document.Subtitle);
        Assert.Equal("Desc", document.Description);
        Assert.Equal(row.InstructorName, document.InstructorName);
        Assert.Equal(row.InstructorHeadline, document.InstructorHeadline);
        Assert.Equal(row.InstructorId, document.InstructorId);
        Assert.Equal(row.CategoryId, document.CategoryId);
        Assert.Equal(row.CategoryNameTh, document.CategoryNameTh);
        Assert.Equal(row.CategoryNameEn, document.CategoryNameEn);
    }

    // ---- Meilisearch off ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Indexer_WhenMeilisearchIsOff_DoesNothingAndNeverReachesTheDatabase()
    {
        using var context = Context();
        var indexer = Indexer(context);

        Assert.False(indexer.IsEnabled);
        await indexer.SyncCourseAsync(Guid.NewGuid(), CancellationToken.None);
        await indexer.BootstrapAsync(CancellationToken.None);
        var result = await indexer.ReindexAllAsync(CancellationToken.None);

        Assert.False(result.Executed);
        Assert.Equal(0, result.Indexed);
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public async Task ReindexJob_WhenMeilisearchIsOff_IsANoOp()
    {
        using var context = Context();
        var job = new CourseSearchReindexJob(Indexer(context), NullLogger<CourseSearchReindexJob>.Instance);

        await job.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DisabledIndex_AnswersNullToSearchSoTheHandlerFallsBack_AndAcceptsWritesSilently()
    {
        var index = new DisabledCourseSearchIndex();

        Assert.False(index.IsEnabled);
        Assert.Null(await index.SearchCourseIdsAsync("python", CancellationToken.None));
        await index.EnsureIndexAsync(CancellationToken.None);
        await index.UpsertCoursesAsync([], true, CancellationToken.None);
        await index.DeleteCoursesAsync([Guid.NewGuid()], CancellationToken.None);
        Assert.Empty(await index.ListIndexedCourseIdsAsync(CancellationToken.None));
        Assert.Null(await index.CountCourseDocumentsAsync(CancellationToken.None));
    }
}
