using Microsoft.Extensions.Logging;
using Siri.Modules.Catalog.Application;

namespace Siri.Modules.Catalog.Infrastructure.Search;

/// <summary>
/// The recurring job <c>course-search-reindex</c> (hourly, registered only in <c>Siri.Workers</c>): reconciles the Meilisearch index with the
/// database — every Published course is (re)written, documents of courses that are no longer Published are removed
/// (<see cref="CourseSearchIndexer.ReindexAllAsync"/>). Approve/unpublish already sync their own course immediately; this run is the safety net
/// that heals anything those best-effort hooks missed (an engine outage, a seeded or imported course, a renamed category or instructor) and fills a
/// fresh index. Does nothing, and costs nothing, while Meilisearch is not configured. An engine failure is rethrown so Hangfire records the run as failed
/// and retries it, instead of the index silently staying behind.
/// </summary>
public sealed class CourseSearchReindexJob(CourseSearchIndexer indexer, ILogger<CourseSearchReindexJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!indexer.IsEnabled)
        {
            return;
        }

        var result = await indexer.ReindexAllAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Course search reindex finished: {Indexed} course(s) indexed, {Removed} stale document(s) removed.", result.Indexed, result.Removed);
    }
}
