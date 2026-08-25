using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetPendingCourseReviews;

/// <summary>Maps GET /api/catalog/admin/courses/pending?page=&amp;pageSize= (see
/// <c>CatalogModule.MapCatalogEndpoints</c> for the group prefix + AdminOnly policy).</summary>
public static class GetPendingCourseReviewsEndpoint
{
    public static IEndpointRouteBuilder MapGetPendingCourseReviewsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/pending", HandleAsync)
            .WithName("CatalogGetPendingCourseReviews")
            .WithSummary("รายการคอร์สที่รอตรวจสอบเพื่อเผยแพร่")
            .Produces<PagedResult<PendingCourseReviewSummary>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        GetPendingCourseReviewsHandler handler,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = GetPendingCourseReviewsHandler.DefaultPageSize)
    {
        var result = await handler.HandleAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
