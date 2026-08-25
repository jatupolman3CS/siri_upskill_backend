using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.ApproveCourse;

/// <summary>Maps POST /api/catalog/admin/courses/{id}/approve (see <c>CatalogModule.MapCatalogEndpoints</c>
/// for the group prefix + AdminOnly policy).</summary>
public static class ApproveCourseEndpoint
{
    public static IEndpointRouteBuilder MapApproveCourseEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{id:guid}/approve", HandleAsync)
            .WithName("CatalogApproveCourse")
            .WithSummary("อนุมัติและเผยแพร่คอร์ส")
            .Produces<ApproveCourseResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        ApproveCourseHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
