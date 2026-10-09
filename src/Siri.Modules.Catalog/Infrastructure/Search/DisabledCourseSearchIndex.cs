using Siri.Modules.Catalog.Application;

namespace Siri.Modules.Catalog.Infrastructure.Search;

/// <summary>
/// <see cref="ICourseSearchIndex"/> used when Meilisearch is not configured (no URL, no real API key, or switched off). Every read answers
/// "I cannot help" (<c>null</c>), so <c>SearchCoursesHandler</c> uses the PostgreSQL <c>pg_trgm</c> path; every write is a no-op, so the
/// approve/unpublish hooks and the reindex job cost nothing. Also the natural stand-in for tests that do not exercise search.
/// </summary>
public sealed class DisabledCourseSearchIndex : ICourseSearchIndex
{
    public bool IsEnabled => false;

    public Task<IReadOnlyList<Guid>?> SearchCourseIdsAsync(string text, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>?>(null);

    public Task EnsureIndexAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpsertCoursesAsync(
        IReadOnlyCollection<CourseSearchDocument> documents, bool waitForCompletion, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task DeleteCoursesAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyCollection<Guid>> ListIndexedCourseIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Guid>>([]);

    public Task<long?> CountCourseDocumentsAsync(CancellationToken cancellationToken) => Task.FromResult<long?>(null);
}
