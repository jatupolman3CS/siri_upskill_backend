using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetPendingInstructorApplications;

/// <summary>Maps GET /api/catalog/admin/instructors/pending?page=&amp;pageSize= (see
/// <c>CatalogModule.MapCatalogEndpoints</c> for the group prefix + AdminOnly policy).</summary>
public static class GetPendingInstructorApplicationsEndpoint
{
    public static IEndpointRouteBuilder MapGetPendingInstructorApplicationsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/pending", HandleAsync)
            .WithName("CatalogGetPendingInstructorApplications")
            .WithSummary("รายการใบสมัครผู้สอนที่รออนุมัติ")
            .Produces<PagedResult<InstructorApplicationSummary>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        GetPendingInstructorApplicationsHandler handler,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = GetPendingInstructorApplicationsHandler.DefaultPageSize)
    {
        var result = await handler.HandleAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
