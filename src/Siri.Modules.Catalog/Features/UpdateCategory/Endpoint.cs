using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UpdateCategory;

/// <summary>Maps PUT /api/catalog/admin/categories/{id} (see <c>CatalogModule.MapCatalogEndpoints</c>
/// for the group prefix + AdminOnly policy). <c>id</c> binds from the route, <c>command</c> from the
/// JSON body — two separately-bound parameters rather than one record carrying both, same shape
/// <c>Identity.Features.RevokeSession.Endpoint</c> uses for its route id.</summary>
public static class UpdateCategoryEndpoint
{
    public static IEndpointRouteBuilder MapUpdateCategoryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{id:guid}", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UpdateCategoryCommand>>()
            .WithName("CatalogUpdateCategory")
            .WithSummary("แก้ไขหมวดหมู่คอร์ส (ชื่อ, slug, ไอคอน, สถานะเปิดใช้งาน, ย้ายหมวดหมู่แม่)")
            .Produces<UpdateCategoryResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateCategoryCommand command,
        UpdateCategoryHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
