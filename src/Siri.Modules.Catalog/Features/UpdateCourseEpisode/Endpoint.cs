using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.CreateCourseEpisode;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UpdateCourseEpisode;

public static class UpdateCourseEpisodeEndpoint
{
    public static IEndpointRouteBuilder MapUpdateCourseEpisodeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{courseId:guid}/sections/{sectionId:guid}/episodes/{episodeId:guid}", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UpdateCourseEpisodeCommand>>()
            .WithName("CatalogUpdateCourseEpisode")
            .WithSummary("แก้ไขข้อมูลบทเรียนในคอร์ส")
            .Produces<CourseEpisodeResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        Guid sectionId,
        Guid episodeId,
        UpdateCourseEpisodeCommand command,
        UpdateCourseEpisodeHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sectionId, episodeId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
