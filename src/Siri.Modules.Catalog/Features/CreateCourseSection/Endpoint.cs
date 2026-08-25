using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CreateCourseSection;

public static class CreateCourseSectionEndpoint
{
    public static IEndpointRouteBuilder MapCreateCourseSectionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{courseId:guid}/sections", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateCourseSectionCommand>>()
            .WithName("CatalogCreateCourseSection")
            .WithSummary("สร้างส่วน/บทหลักใหม่ในคอร์ส")
            .Produces<CourseSectionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        CreateCourseSectionCommand command,
        CreateCourseSectionHandler handler,
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
            ? Results.Created($"/api/catalog/instructor/courses/{courseId}/sections/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
