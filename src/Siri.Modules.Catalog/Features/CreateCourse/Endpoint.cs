using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CreateCourse;

/// <summary>Maps POST /api/catalog/instructor/courses (see <c>CatalogModule.MapCatalogEndpoints</c> for
/// the group prefix + InstructorOnly policy).</summary>
public static class CreateCourseEndpoint
{
    public static IEndpointRouteBuilder MapCreateCourseEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateCourseCommand>>()
            .WithName("CatalogCreateCourse")
            .WithSummary("สร้างคอร์สฉบับร่างใหม่")
            .Produces<CreateCourseResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        CreateCourseCommand command,
        CreateCourseHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/catalog/instructor/courses/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
