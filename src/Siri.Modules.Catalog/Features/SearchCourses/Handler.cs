using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.SearchCourses;

/// <summary>
/// Searches and filters <see cref="CourseStatus.Published"/> courses — never any other status, regardless
/// of caller, since this endpoint is public (security.md: only the server decides what's visible).
/// <para>
/// <b>Full-text search — kept deliberately narrow and isolated</b>: the only SQL-Server-specific surface
/// this handler touches is one simple, self-contained <c>FREETEXTTABLE</c> query via
/// <c>Database.SqlQuery&lt;T&gt;</c> (provider-agnostic API, raw SQL text — database.md's own sanctioned
/// escape hatch, same as <c>FromSqlInterpolated</c>) that resolves to nothing more than a
/// <c>(CourseId, Rank)</c> lookup, fully materialized into memory before anything else happens.
/// <c>FREETEXTTABLE</c> was chosen over <c>CONTAINSTABLE</c> specifically because it accepts arbitrary
/// natural-language text with no boolean-operator predicate syntax to get wrong — a user's raw search
/// box input is exactly that. Every other query in this handler (filtering, faceting, sorting,
/// pagination) is completely ordinary LINQ-to-Entities with no FTS-specific composition — deliberately,
/// so the one piece of this handler that cannot be verified without a live, FTS-enabled SQL Server (this
/// dev environment has neither) stays as small and simple as it can be.
/// </para>
/// <para>
/// <b>Relevance sort</b> needs special handling because of that same isolation: <c>Rank</c> only exists
/// in the in-memory dictionary from the FTS query, not as a column the database can <c>ORDER BY</c>
/// directly through LINQ. So when sorting by <see cref="CourseSearchSort.Relevance"/>, this handler
/// fetches the *filtered* id set only (cheap — just ids), sorts those ids by rank in memory, slices the
/// requested page of ids, then fetches full rows for exactly that page and re-orders them to match.
/// Every other sort order (price, rating, newest) is a normal database-level <c>ORDER BY</c> +
/// <c>OFFSET/FETCH</c>, same as every other paginated handler in this module.
/// </para>
/// </summary>
public sealed class SearchCoursesHandler(AppDbContext dbContext)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

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
            var matches = await dbContext.Database
                .SqlQuery<CourseSearchMatch>(
                    $"SELECT [KEY] AS CourseId, [RANK] AS Rank FROM FREETEXTTABLE(catalog.Courses, (Title, Subtitle, Description), {query.Q})")
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            rankByCourseId = matches.ToDictionary(m => m.CourseId, m => m.Rank);
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

        var filtered = baseCourses;

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

        var items = pageCourses
            .Select(c => new CourseSearchResultItem(
                c.Id, c.Slug, c.Title, c.Subtitle, c.ThumbnailUrl, c.Price, c.ComparePrice, c.Currency,
                c.Level, c.Language, c.RatingAverage, c.RatingCount, c.EnrollmentCount, c.EpisodeCount,
                c.TotalDurationSeconds, c.InstructorId, instructorNames.GetValueOrDefault(c.InstructorId, string.Empty),
                c.CategoryId))
            .ToList();

        return new SearchCoursesResponse(
            PagedResult<CourseSearchResultItem>.Create(items, totalCount, effectivePage, effectivePageSize),
            new CourseSearchFacets(categoryFacets, levelFacets, instructorFacets));
    }

    private async Task<List<Course>> FetchPageByRelevanceAsync(
        IQueryable<Course> filtered, Dictionary<Guid, int> rankByCourseId, int page, int pageSize, CancellationToken cancellationToken)
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

    private static async Task<List<Course>> FetchPageBySortAsync(
        IQueryable<Course> filtered, CourseSearchSort sort, int page, int pageSize, CancellationToken cancellationToken)
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
