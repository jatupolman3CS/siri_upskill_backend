using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.RejectInstructorApplication;

/// <summary>Maps POST /api/catalog/admin/instructors/{id}/reject (see
/// <c>CatalogModule.MapCatalogEndpoints</c> for the group prefix + AdminOnly policy).</summary>
public static class RejectInstructorApplicationEndpoint
{
    public static IEndpointRouteBuilder MapRejectInstructorApplicationEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{id:guid}/reject", HandleAsync)
            .WithName("CatalogRejectInstructorApplication")
            .WithSummary("ปฏิเสธใบสมัครผู้สอน")
            .Produces<RejectInstructorApplicationResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        RejectInstructorApplicationHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
