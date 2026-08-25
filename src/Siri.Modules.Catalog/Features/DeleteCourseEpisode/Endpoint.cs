using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DeleteCourseEpisode;

public static class DeleteCourseEpisodeEndpoint
{
    public static IEndpointRouteBuilder MapDeleteCourseEpisodeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/{courseId:guid}/sections/{sectionId:guid}/episodes/{episodeId:guid}", HandleAsync)
            .WithName("CatalogDeleteCourseEpisode")
            .WithSummary("ลบบทเรียนออกจากส่วน/บทหลัก")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        Guid sectionId,
        Guid episodeId,
        DeleteCourseEpisodeHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sectionId, episodeId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
