using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;

namespace Siri.Modules.Catalog.Features.SearchCourses;

/// <summary>Maps GET /api/catalog/courses/search?q=&amp;categoryId=&amp;instructorId=&amp;level=&amp;minPrice=
/// &amp;maxPrice=&amp;minRating=&amp;sort=&amp;page=&amp;pageSize= (see <c>CatalogModule.MapCatalogEndpoints</c> for the
/// group prefix). Public — <c>.AllowAnonymous()</c> opts out of the group's default
/// <c>.RequireAuthorization()</c>, same reasoning <c>GetCategoryTreeEndpoint</c>'s own doc comment gives:
/// course discovery must work for a signed-out visitor. Only ever returns
/// <see cref="CourseStatus.Published"/> courses (<c>SearchCoursesHandler</c>'s own filter) — Draft/
/// InReview/Rejected courses never leak through search regardless of caller. <c>.CacheOutput(...)</c>
/// (task P1-07) is safe here specifically because the response never varies by caller identity — every
/// visitor sees the same result for the same query string, which ASP.NET Core's output cache already
/// keys on by default.</summary>
public static class SearchCoursesEndpoint
{
    public static IEndpointRouteBuilder MapSearchCoursesEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/courses/search", HandleAsync)
            .AllowAnonymous()
            .CacheOutput(CourseOutputCache.PolicyName)
            .WithName("CatalogSearchCourses")
            .WithSummary("ค้นหาและกรองคอร์สที่เผยแพร่แล้ว")
            .Produces<SearchCoursesResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        SearchCoursesHandler handler,
        CancellationToken cancellationToken,
        string? q = null,
        Guid? categoryId = null,
        Guid? instructorId = null,
        CourseLevel? level = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        decimal? minRating = null,
        CourseSearchSort sort = CourseSearchSort.Relevance,
        int page = 1,
        int pageSize = SearchCoursesHandler.DefaultPageSize,
        DeliveryFormat? format = null)
    {
        var query = new SearchCoursesQuery(q, categoryId, instructorId, level, minPrice, maxPrice, minRating, sort, page, pageSize, format);
        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }
}
