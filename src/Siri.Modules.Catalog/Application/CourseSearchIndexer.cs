using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Catalog.Infrastructure.Search;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Application;

/// <summary>
/// Keeps the search index (<see cref="ICourseSearchIndex"/>) in step with the database. The database is always the source of truth: every
/// operation here <em>reads the course from PostgreSQL</em> and makes the index match, so the operations are idempotent and order-independent
/// (replaying them, or two instances running at once, converges to the same index).
/// <list type="bullet">
/// <item><see cref="SyncCourseAsync"/> — one course, called right after a visibility change (approve / unpublish). Best effort: a Meilisearch
/// outage must never fail the admin's action, so it logs and the hourly reindex heals the gap.</item>
/// <item><see cref="ReindexAllAsync"/> — full reconcile (upsert every Published course, remove documents of courses that are no longer
/// Published). Run hourly by the <c>course-search-reindex</c> job, by <see cref="BootstrapAsync"/> on an empty index, and by the admin endpoint.</item>
/// </list>
/// Only <see cref="CourseStatus.Published"/> courses are indexed — the same rule the public search enforces again on the database side.
/// </summary>
public sealed class CourseSearchIndexer(
    AppDbContext dbContext,
    ICourseSearchIndex index,
    IOptions<MeilisearchOptions> options,
    ILogger<CourseSearchIndexer> logger)
{
    /// <summary>Courses read from the database and sent to the engine per round trip.</summary>
    public const int ReindexBatchSize = 500;

    public bool IsEnabled => index.IsEnabled;

    /// <summary>
    /// Makes the index agree with the database for one course: Published → upsert, anything else (unpublished, deleted, missing) → delete.
    /// Never throws for search-engine trouble (see class remarks); caller cancellation still propagates.
    /// </summary>
    public async Task SyncCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (!index.IsEnabled)
        {
            return;
        }

        try
        {
            var row = await PublishedRows(courseId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                await index.DeleteCoursesAsync([courseId], cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await index.UpsertCoursesAsync([ToDocument(row)], waitForCompletion: false, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (CourseSearchIndexException ex)
        {
            logger.LogWarning(
                "Search index sync for course {CourseId} failed ({Reason}); the hourly reindex will correct it.", courseId, ex.Message);
        }
    }

    /// <summary>Full reconcile — see class remarks. Throws <see cref="CourseSearchIndexException"/> if the engine fails, so a job run is visibly failed.</summary>
    public async Task<CourseSearchReindexResult> ReindexAllAsync(CancellationToken cancellationToken)
    {
        if (!index.IsEnabled)
        {
            return CourseSearchReindexResult.NotRun;
        }

        await index.EnsureIndexAsync(cancellationToken).ConfigureAwait(false);

        var publishedIds = new HashSet<Guid>();
        for (var skip = 0; ; skip += ReindexBatchSize)
        {
            var rows = await PublishedRows(null).Skip(skip).Take(ReindexBatchSize).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                break;
            }

            await index.UpsertCoursesAsync(rows.Select(ToDocument).ToList(), waitForCompletion: true, cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                publishedIds.Add(row.CourseId);
            }

            if (rows.Count < ReindexBatchSize)
            {
                break;
            }
        }

        var staleCandidates = (await index.ListIndexedCourseIdsAsync(cancellationToken).ConfigureAwait(false))
            .Where(id => !publishedIds.Contains(id))
            .ToList();

        var removable = await FilterStillUnpublishedAsync(staleCandidates, cancellationToken).ConfigureAwait(false);
        await index.DeleteCoursesAsync(removable, cancellationToken).ConfigureAwait(false);

        return new CourseSearchReindexResult(Executed: true, Indexed: publishedIds.Count, Removed: removable.Count);
    }

    /// <summary>Startup helper: makes sure the index and its settings exist and, when no course is in it yet, fills it. A populated index is left alone.</summary>
    public async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        if (!index.IsEnabled)
        {
            return;
        }

        await index.EnsureIndexAsync(cancellationToken).ConfigureAwait(false);

        var existing = await index.CountCourseDocumentsAsync(cancellationToken).ConfigureAwait(false);
        if (existing is > 0)
        {
            logger.LogInformation("Meilisearch index is ready with {Count} course document(s).", existing);
            return;
        }

        var result = await ReindexAllAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Meilisearch index was empty; initial reindex indexed {Indexed} course(s) and removed {Removed}.", result.Indexed, result.Removed);
    }

    /// <summary>Operator-facing health: is it on, can we reach it, and does the index hold as many courses as the database has Published.</summary>
    public async Task<CourseSearchStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var publishedInDatabase = await dbContext.Courses().AsNoTracking()
            .CountAsync(c => c.Status == CourseStatus.Published, cancellationToken).ConfigureAwait(false);

        if (!index.IsEnabled)
        {
            return new CourseSearchStatus(
                Enabled: false, Reachable: false, options.Value.DocumentsIndexUid, IndexedCourses: null, publishedInDatabase, options.Value.InactiveReason);
        }

        try
        {
            var indexed = await index.CountCourseDocumentsAsync(cancellationToken).ConfigureAwait(false);
            return new CourseSearchStatus(
                Enabled: true,
                Reachable: true,
                options.Value.DocumentsIndexUid,
                indexed ?? 0,
                publishedInDatabase,
                indexed is null ? "Meilisearch is reachable but the index does not exist yet; run a reindex." : null);
        }
        catch (CourseSearchIndexException ex)
        {
            return new CourseSearchStatus(
                Enabled: true, Reachable: false, options.Value.DocumentsIndexUid, IndexedCourses: null, publishedInDatabase, ex.Message);
        }
    }

    /// <summary>Published courses joined with their instructor and category, ordered by id (stable paging). <paramref name="courseId"/> narrows to one course.
    /// Internal so a unit test can prove the query translates to SQL without a database.</summary>
    internal IQueryable<CourseIndexRow> PublishedRows(Guid? courseId) =>
        from c in dbContext.Courses().AsNoTracking()
        where c.Status == CourseStatus.Published && (courseId == null || c.Id == courseId)
        join p in dbContext.InstructorProfiles().AsNoTracking() on c.InstructorId equals p.Id
        join cat in dbContext.Categories().AsNoTracking() on c.CategoryId equals cat.Id
        orderby c.Id
        select new CourseIndexRow(
            c.Id, c.Slug, c.Title, c.Subtitle, c.Description, c.InstructorId, p.DisplayName, p.Headline, c.CategoryId, cat.NameTh, cat.NameEn);

    /// <summary>Of <paramref name="candidateIds"/> (in the index but not seen as Published during this run), those that really are not Published now.
    /// Guards against deleting a course approved while the reindex was running.</summary>
    public async Task<List<Guid>> FilterStillUnpublishedAsync(List<Guid> candidateIds, CancellationToken cancellationToken)
    {
        if (candidateIds.Count == 0)
        {
            return candidateIds;
        }

        var nowPublished = await dbContext.Courses().AsNoTracking()
            .Where(c => c.Status == CourseStatus.Published && candidateIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidateIds.Except(nowPublished).ToList();
    }

    internal static CourseSearchDocument ToDocument(CourseIndexRow row) =>
        CourseSearchDocument.ForCourse(
            row.CourseId,
            row.Slug,
            row.Title,
            row.Subtitle,
            row.Description,
            row.InstructorId,
            row.InstructorName,
            row.InstructorHeadline,
            row.CategoryId,
            row.CategoryNameTh,
            row.CategoryNameEn);

    internal sealed record CourseIndexRow(
        Guid CourseId,
        string Slug,
        string Title,
        string? Subtitle,
        string? Description,
        Guid InstructorId,
        string InstructorName,
        string? InstructorHeadline,
        Guid CategoryId,
        string? CategoryNameTh,
        string? CategoryNameEn);
}

/// <summary><see cref="Executed"/> is false when Meilisearch is not configured (nothing was attempted).</summary>
public sealed record CourseSearchReindexResult(bool Executed, int Indexed, int Removed)
{
    public static CourseSearchReindexResult NotRun { get; } = new(false, 0, 0);
}

/// <summary>What <c>GET /api/catalog/admin/search/status</c> returns. Contains no URL and no key.</summary>
public sealed record CourseSearchStatus(
    bool Enabled,
    bool Reachable,
    string IndexUid,
    long? IndexedCourses,
    int PublishedCoursesInDatabase,
    string? Message);
