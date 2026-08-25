using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DeleteCategory;

/// <summary>Maps DELETE /api/catalog/admin/categories/{id} (see <c>CatalogModule.MapCatalogEndpoints</c>
/// for the group prefix + AdminOnly policy).</summary>
public static class DeleteCategoryEndpoint
{
    public static IEndpointRouteBuilder MapDeleteCategoryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/{id:guid}", HandleAsync)
            .WithName("CatalogDeleteCategory")
            .WithSummary("ลบหมวดหมู่คอร์ส (ต้องไม่มีหมวดหมู่ย่อย)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        DeleteCategoryHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
