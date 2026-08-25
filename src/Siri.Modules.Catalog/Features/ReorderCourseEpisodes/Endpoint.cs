using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.ReorderCourseEpisodes;

public static class ReorderCourseEpisodesEndpoint
{
    public static IEndpointRouteBuilder MapReorderCourseEpisodesEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{courseId:guid}/sections/{sectionId:guid}/episodes/reorder", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ReorderCourseEpisodesCommand>>()
            .WithName("CatalogReorderCourseEpisodes")
            .WithSummary("จัดลำดับบทเรียนทั้งหมดในส่วน/บทหลัก")
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        Guid sectionId,
        ReorderCourseEpisodesCommand command,
        ReorderCourseEpisodesHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sectionId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok()
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
