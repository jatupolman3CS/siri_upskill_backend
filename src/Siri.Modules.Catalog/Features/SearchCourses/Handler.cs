using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.SearchCourses;

/// <summary>
/// Searches and filters <see cref="CourseStatus.Published"/> courses — never any other status, regardless
/// of caller, since this endpoint is public (security.md: only the server decides what's visible).
/// <para>
/// <b>Text matching</b> (the <c>q</c> parameter — course title/subtitle/description/category <em>and the instructor's name</em>) is done by
/// Meilisearch (<see cref="ICourseSearchIndex"/>), which returns the matching course ids best-first. Everything else — Published-only,
/// category/instructor/level/price/rating filters, facets, sort, paging, wishlist flags — stays in PostgreSQL, applied to that candidate set, so the
/// index being a little stale can never expose an unpublished course. When Meilisearch is off or cannot answer, or finds nothing, the
/// <c>pg_trgm</c> path below runs instead: it keeps the search working through an outage or a cold index, and its substring matching still finds
/// word fragments Meilisearch's prefix matching cannot (see <see cref="FindMatchesInDatabaseAsync"/>).
/// </para>
/// </summary>
public sealed class SearchCoursesHandler(
    AppDbContext dbContext,
    IUserContext userContext,
    IClock clock,
    ICourseSearchIndex searchIndex)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Rank given to a course found only because its instructor's name matched, in the database fallback. Same order of magnitude as a
    /// trigram title match (similarity × 100), so a teacher search is not buried under weak description hits.</summary>
    internal const int InstructorNameMatchRank = 60;

    private sealed record CourseSearchMatch(Guid CourseId, int Rank);

    public async Task<SearchCoursesResponse> HandleAsync(SearchCoursesQuery query, CancellationToken cancellationToken)
    {
        var effectivePage = query.Page < 1 ? 1 : query.Page;
        var effectivePageSize = query.PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => query.PageSize,
        };

        var hasSearchText = !string.IsNullOrWhiteSpace(query.Q);
        Dictionary<Guid, int>? rankByCourseId = null;

        var baseCourses = dbContext.Courses().AsNoTracking().Where(c => c.Status == CourseStatus.Published);

        if (hasSearchText)
        {
            var queryText = query.Q!.Trim();

            // Meilisearch first. null = it could not answer (off / down / timed out) and an empty list = no match;
            // both fall through to the database so the search keeps working and still finds fragments the engine cannot.
            var engineMatches = await searchIndex.SearchCourseIdsAsync(queryText, cancellationToken).ConfigureAwait(false);
            rankByCourseId = engineMatches is { Count: > 0 }
                ? RankByPosition(engineMatches)
                : await FindMatchesInDatabaseAsync(queryText, cancellationToken).ConfigureAwait(false);

            var matchedIds = rankByCourseId.Keys.ToList();
            baseCourses = baseCourses.Where(c => matchedIds.Contains(c.Id));
        }

        // Facets read from baseCourses (search-matched + Published) — before category/level/price/
        // rating filters. See SearchCoursesResponse's own doc comment for why.
        var categoryFacets = await baseCourses
            .GroupBy(c => c.CategoryId)
            .Select(g => new CategoryFacet(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var levelFacets = await baseCourses
            .GroupBy(c => c.Level)
            .Select(g => new LevelFacet(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Counts only here — display names resolved later in the single name lookup shared with the
        // page items, so adding this facet (P1-10) costs one GROUP BY and no extra name query.
        var instructorFacetGroups = await baseCourses
            .GroupBy(c => c.InstructorId)
            .Select(g => new { InstructorId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var formatFacets = await baseCourses
            .GroupBy(c => c.DeliveryFormat)
            .Select(g => new FormatFacet(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var filtered = baseCourses;

        if (query.Format is { } format)
        {
            filtered = filtered.Where(c => c.DeliveryFormat == format);
        }

        if (query.CategoryId is { } categoryId)
        {
            filtered = filtered.Where(c => c.CategoryId == categoryId);
        }

        if (query.InstructorId is { } instructorFilterId)
        {
            filtered = filtered.Where(c => c.InstructorId == instructorFilterId);
        }

        if (query.Level is { } level)
        {
            filtered = filtered.Where(c => c.Level == level);
        }

        if (query.MinPrice is { } minPrice)
        {
            filtered = filtered.Where(c => c.Price >= minPrice);
        }

        if (query.MaxPrice is { } maxPrice)
        {
            filtered = filtered.Where(c => c.Price <= maxPrice);
        }

        if (query.MinRating is { } minRating)
        {
            filtered = filtered.Where(c => c.RatingAverage >= minRating);
        }

        var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

        var pageCourses = query.Sort == CourseSearchSort.Relevance && rankByCourseId is not null
            ? await FetchPageByRelevanceAsync(filtered, rankByCourseId, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false)
            : await FetchPageBySortAsync(filtered, query.Sort, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);

        var instructorIds = pageCourses.Select(c => c.InstructorId)
            .Concat(instructorFacetGroups.Select(g => g.InstructorId))
            .Distinct()
            .ToList();
        var instructorNames = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .Where(p => instructorIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        var instructorFacets = instructorFacetGroups
            .Select(g => new InstructorFacet(g.InstructorId, instructorNames.GetValueOrDefault(g.InstructorId, string.Empty), g.Count))
            .ToList();

        var pageCourseIds = pageCourses.Select(c => c.Id).ToList();
        var wishlistedSet = userContext.UserId.HasValue && pageCourseIds.Count > 0
            ? (await dbContext.Wishlists()
                .AsNoTracking()
                .Where(w => w.UserId == userContext.UserId.Value && pageCourseIds.Contains(w.CourseId))
                .Select(w => w.CourseId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)).ToHashSet()
            : [];

        var nowUtc = clock.UtcNow;
        var nextStartsAtByCourseId = pageCourseIds.Count > 0
            ? await dbContext.CourseLiveSessions()
                .AsNoTracking()
                .Where(s => pageCourseIds.Contains(s.CourseId) && s.Status == CourseLiveSessionStatus.Scheduled && s.StartsAtUtc > nowUtc)
                .GroupBy(s => s.CourseId)
                .Select(g => new { CourseId = g.Key, NextStartsAtUtc = g.Min(s => s.StartsAtUtc) })
                .ToDictionaryAsync(x => x.CourseId, x => (DateTime?)x.NextStartsAtUtc, cancellationToken)
                .ConfigureAwait(false)
            : [];

        var items = pageCourses
            .Select(c => new CourseSearchResultItem(
                c.Id, c.Slug, c.Title, c.Subtitle, c.ThumbnailUrl, c.Price, c.ComparePrice, c.Currency,
                c.Level, c.Language, c.RatingAverage, c.RatingCount, c.EnrollmentCount, c.EpisodeCount,
                c.TotalDurationSeconds, c.InstructorId, instructorNames.GetValueOrDefault(c.InstructorId, string.Empty),
                c.CategoryId, wishlistedSet.Contains(c.Id),
                c.DeliveryFormat,
                c.DeliveryFormat == DeliveryFormat.OnDemand ? null : nextStartsAtByCourseId.GetValueOrDefault(c.Id)))
            .ToList();

        return new SearchCoursesResponse(
            PagedResult<CourseSearchResultItem>.Create(items, totalCount, effectivePage, effectivePageSize),
            new CourseSearchFacets(categoryFacets, levelFacets, instructorFacets, formatFacets));
    }

    /// <summary>Turns the engine's best-first id list into ranks (higher = more relevant) the shared relevance sort understands.</summary>
    internal static Dictionary<Guid, int> RankByPosition(IReadOnlyList<Guid> bestFirst)
    {
        var ranks = new Dictionary<Guid, int>(bestFirst.Count);
        for (var position = 0; position < bestFirst.Count; position++)
        {
            ranks.TryAdd(bestFirst[position], bestFirst.Count - position);
        }

        return ranks;
    }

    /// <summary>
    /// The PostgreSQL text search — the fallback behind Meilisearch. Matches the course's own text via trigram similarity and, since a visitor
    /// also searches by teacher, any course whose instructor's display name contains the text.
    /// </summary>
    private async Task<Dictionary<Guid, int>> FindMatchesInDatabaseAsync(string queryText, CancellationToken cancellationToken)
    {
        var pattern = $"%{queryText}%";

        // Trigram search (pg_trgm), not PostgreSQL's own tsvector full-text search: the FTS parser
        // splits on whitespace, and Thai writes without spaces between words, so to_tsvector would
        // reduce a whole Thai phrase to one token and match nothing. pg_trgm works on 3-character
        // sequences instead, which handles Thai and gives a closeness score for free.
        // The extension and the backing GIN indexes are created by this app's own migration
        // (AppDbContext.HasPostgresExtension + CourseConfiguration's gin_trgm_ops indexes), so
        // unlike the SQL Server Full-Text Search component this replaces, it is guaranteed present —
        // no capability probing and no silent LIKE fallback (which is what X-7/P0-40 were about).
        // Every identifier is double-quoted, including the aliases: the whole schema is UPPERCASE
        // and PostgreSQL folds unquoted identifiers to lower case, which would break both the table
        // lookup and SqlQuery<T>'s column-to-property mapping.
        var matches = await dbContext.Database
            .SqlQuery<CourseSearchMatch>($"""
                SELECT "ID" AS "CourseId",
                       (similarity("TITLE", {queryText}) * 100
                        + similarity(COALESCE("SUBTITLE", ''), {queryText}) * 50
                        + similarity(COALESCE("DESCRIPTION", ''), {queryText}) * 10)::int AS "Rank"
                FROM "CATALOG"."COURSES"
                WHERE "IS_DELETED" = false
                  AND "STATUS" = 'Published'
                  AND ("TITLE" ILIKE {pattern}
                       OR "SUBTITLE" ILIKE {pattern}
                       OR "DESCRIPTION" ILIKE {pattern})
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ranks = matches.ToDictionary(m => m.CourseId, m => m.Rank);

        // Instructor-name match, expressed in LINQ (not the raw SQL above) so it stays provider-agnostic and needs no hand-written
        // identifiers. Case-insensitive via lower-casing both sides; a visitor typing a teacher's name is the whole use case.
        var lowered = queryText.ToLowerInvariant();
        var instructorMatches = await InstructorNameMatchQuery(lowered).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var courseId in instructorMatches)
        {
            ranks[courseId] = ranks.TryGetValue(courseId, out var existing) ? Math.Max(existing, InstructorNameMatchRank) : InstructorNameMatchRank;
        }

        return ranks;
    }

    /// <summary>Ids of Published courses whose instructor's display name contains <paramref name="loweredText"/>. Internal for the translation unit test.</summary>
    internal IQueryable<Guid> InstructorNameMatchQuery(string loweredText) =>
        from c in dbContext.Courses().AsNoTracking()
        where c.Status == CourseStatus.Published
        join p in dbContext.InstructorProfiles().AsNoTracking() on c.InstructorId equals p.Id
        where p.DisplayName.ToLower().Contains(loweredText)
        select c.Id;

    private async Task<List<COURSE>> FetchPageByRelevanceAsync(
        IQueryable<COURSE> filtered, Dictionary<Guid, int> rankByCourseId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var filteredIds = await filtered.Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

        var pageIds = filteredIds
            .OrderByDescending(id => rankByCourseId[id])
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var unordered = await dbContext.Courses().AsNoTracking()
            .Where(c => pageIds.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byId = unordered.ToDictionary(c => c.Id);
        return pageIds.Select(id => byId[id]).ToList();
    }

    private static async Task<List<COURSE>> FetchPageBySortAsync(
        IQueryable<COURSE> filtered, CourseSearchSort sort, int page, int pageSize, CancellationToken cancellationToken)
    {
        // Relevance falls through to here only when there was no search text to rank by — Newest is the
        // reasonable default for "browse with filters, no search term" (same as GetPendingCourseReviews'
        // own ordering choice, oldest/newest by a real timestamp rather than an undefined rank).
        var sorted = sort switch
        {
            CourseSearchSort.PriceAsc => filtered.OrderBy(c => c.Price),
            CourseSearchSort.PriceDesc => filtered.OrderByDescending(c => c.Price),
            CourseSearchSort.RatingDesc => filtered.OrderByDescending(c => c.RatingAverage),
            _ => filtered.OrderByDescending(c => c.PublishedAtUtc),
        };

        return await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
