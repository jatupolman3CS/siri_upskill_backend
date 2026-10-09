using Siri.Modules.Catalog.Application;

namespace Siri.IntegrationTests.TestData;

/// <summary>
/// An in-memory <see cref="ICourseSearchIndex"/> that records what the Catalog code asked of the search engine and answers from values the test sets — so
/// the integration tests can prove the handler/indexer logic against a real PostgreSQL without a Meilisearch server.
/// </summary>
public sealed class RecordingCourseSearchIndex : ICourseSearchIndex
{
    /// <summary>What <see cref="SearchCourseIdsAsync"/> answers: <c>null</c> = engine unavailable, a list = ranked hits.</summary>
    public IReadOnlyList<Guid>? SearchResult { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Course ids <see cref="ListIndexedCourseIdsAsync"/> reports as currently in the index.</summary>
    public List<Guid> IndexedIds { get; } = [];

    public long? CourseCount { get; set; }

    /// <summary>When true every write throws <see cref="CourseSearchIndexException"/> (an engine outage).</summary>
    public bool FailWrites { get; set; }

    public List<string> SearchedTexts { get; } = [];

    public List<CourseSearchDocument> Upserted { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public int EnsureCalls { get; private set; }

    public bool IsEnabled => Enabled;

    public Task<IReadOnlyList<Guid>?> SearchCourseIdsAsync(string text, CancellationToken cancellationToken)
    {
        SearchedTexts.Add(text);
        return Task.FromResult(SearchResult);
    }

    public Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        EnsureCalls++;
        return Task.CompletedTask;
    }

    public Task UpsertCoursesAsync(IReadOnlyCollection<CourseSearchDocument> documents, bool waitForCompletion, CancellationToken cancellationToken)
    {
        ThrowIfFailing();
        Upserted.AddRange(documents);
        return Task.CompletedTask;
    }

    public Task DeleteCoursesAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken)
    {
        ThrowIfFailing();
        Deleted.AddRange(courseIds);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<Guid>> ListIndexedCourseIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Guid>>(IndexedIds.ToList());

    public Task<long?> CountCourseDocumentsAsync(CancellationToken cancellationToken) => Task.FromResult(CourseCount);

    private void ThrowIfFailing()
    {
        if (FailWrites)
        {
            throw new CourseSearchIndexException("simulated Meilisearch outage");
        }
    }
}
