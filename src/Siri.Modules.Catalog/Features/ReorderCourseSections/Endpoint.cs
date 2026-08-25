using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.ReorderCourseSections;

public static class ReorderCourseSectionsEndpoint
{
    public static IEndpointRouteBuilder MapReorderCourseSectionsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{courseId:guid}/sections/reorder", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ReorderCourseSectionsCommand>>()
            .WithName("CatalogReorderCourseSections")
            .WithSummary("จัดลำดับส่วน/บทหลักทั้งหมดในคอร์ส")
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        ReorderCourseSectionsCommand command,
        ReorderCourseSectionsHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok()
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
