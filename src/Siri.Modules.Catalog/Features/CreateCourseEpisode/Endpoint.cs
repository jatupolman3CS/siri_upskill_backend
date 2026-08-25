using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CreateCourseEpisode;

public static class CreateCourseEpisodeEndpoint
{
    public static IEndpointRouteBuilder MapCreateCourseEpisodeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{courseId:guid}/sections/{sectionId:guid}/episodes", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateCourseEpisodeCommand>>()
            .WithName("CatalogCreateCourseEpisode")
            .WithSummary("สร้างบทเรียนใหม่ในส่วน/บทหลัก")
            .Produces<CourseEpisodeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        Guid sectionId,
        CreateCourseEpisodeCommand command,
        CreateCourseEpisodeHandler handler,
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
            ? Results.Created($"/api/catalog/instructor/courses/{courseId}/sections/{sectionId}/episodes/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
