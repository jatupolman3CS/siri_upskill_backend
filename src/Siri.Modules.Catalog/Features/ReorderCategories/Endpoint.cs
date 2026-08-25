using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.ReorderCategories;

/// <summary>Maps POST /api/catalog/admin/categories/reorder (see <c>CatalogModule.MapCatalogEndpoints</c>
/// for the group prefix + AdminOnly policy). POST, not PUT/PATCH — this is a bulk action against an
/// implicit collection (one parent's siblings), not a single addressable resource, the same shape
/// <c>Identity.Features.RevokeAllSessions</c>/<c>RevokeOtherSessions</c> already establish for the
/// identical reason.</summary>
public static class ReorderCategoriesEndpoint
{
    public static IEndpointRouteBuilder MapReorderCategoriesEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/reorder", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ReorderCategoriesCommand>>()
            .WithName("CatalogReorderCategories")
            .WithSummary("จัดลำดับหมวดหมู่คอร์สภายใต้พ่อแม่เดียวกันใหม่ทั้งหมด")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        ReorderCategoriesCommand command,
        ReorderCategoriesHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
