using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Infrastructure;

namespace Siri.Modules.Catalog.Features.GetAdminCategoryTree;

/// <summary>Maps GET /api/catalog/admin/categories (see <c>CatalogModule.MapCatalogEndpoints</c> for
/// the group prefix + AdminOnly policy).</summary>
public static class GetAdminCategoryTreeEndpoint
{
    public static IEndpointRouteBuilder MapGetAdminCategoryTreeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleAsync)
            .WithName("CatalogGetAdminCategoryTree")
            .WithSummary("รายการหมวดหมู่คอร์สทั้งหมด รวมที่ปิดใช้งานอยู่ สำหรับหน้าแอดมิน")
            .Produces<IReadOnlyList<CategoryTreeNode>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(GetAdminCategoryTreeHandler handler, CancellationToken cancellationToken)
    {
        var tree = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(tree);
    }
}
