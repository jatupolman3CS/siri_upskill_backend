using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.RejectCourse;

/// <summary>Maps POST /api/catalog/admin/courses/{id}/reject (see <c>CatalogModule.MapCatalogEndpoints</c>
/// for the group prefix + AdminOnly policy).</summary>
public static class RejectCourseEndpoint
{
    public static IEndpointRouteBuilder MapRejectCourseEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{id:guid}/reject", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<RejectCourseCommand>>()
            .WithName("CatalogRejectCourse")
            .WithSummary("ปฏิเสธคอร์ส พร้อมเหตุผล")
            .Produces<RejectCourseResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        RejectCourseCommand command,
        RejectCourseHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
