using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CreateCategory;

/// <summary>Maps POST /api/catalog/admin/categories (see <c>CatalogModule.MapCatalogEndpoints</c> for
/// the group prefix + AdminOnly policy).</summary>
public static class CreateCategoryEndpoint
{
    public static IEndpointRouteBuilder MapCreateCategoryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateCategoryCommand>>()
            .WithName("CatalogCreateCategory")
            .WithSummary("สร้างหมวดหมู่คอร์สใหม่")
            .Produces<CreateCategoryResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        CreateCategoryCommand command,
        CreateCategoryHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/catalog/admin/categories/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
