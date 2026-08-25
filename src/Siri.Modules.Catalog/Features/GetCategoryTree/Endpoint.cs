using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Infrastructure;

namespace Siri.Modules.Catalog.Features.GetCategoryTree;

/// <summary>Maps GET /api/catalog/categories (see <c>CatalogModule.MapCatalogEndpoints</c> for the
/// group prefix). Public — <c>.AllowAnonymous()</c> opts out of the group's default
/// <c>.RequireAuthorization()</c>, since browsing categories must work for a signed-out visitor.</summary>
public static class GetCategoryTreeEndpoint
{
    public static IEndpointRouteBuilder MapGetCategoryTreeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/categories", HandleAsync)
            .AllowAnonymous()
            .WithName("CatalogGetCategoryTree")
            .WithSummary("รายการหมวดหมู่คอร์สทั้งหมด (เฉพาะที่เปิดใช้งาน) แบบโครงสร้างต้นไม้")
            .Produces<IReadOnlyList<CategoryTreeNode>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(GetCategoryTreeHandler handler, CancellationToken cancellationToken)
    {
        var json = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Content(json, "application/json");
    }
}
