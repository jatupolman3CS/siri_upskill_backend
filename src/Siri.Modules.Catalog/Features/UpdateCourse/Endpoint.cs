using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UpdateCourse;

/// <summary>Maps PUT /api/catalog/instructor/courses/{id} (see <c>CatalogModule.MapCatalogEndpoints</c>
/// for the group prefix + InstructorOnly policy).</summary>
public static class UpdateCourseEndpoint
{
    public static IEndpointRouteBuilder MapUpdateCourseEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{id:guid}", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UpdateCourseCommand>>()
            .WithName("CatalogUpdateCourse")
            .WithSummary("แก้ไขคอร์สฉบับร่าง")
            .Produces<UpdateCourseResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateCourseCommand command,
        UpdateCourseHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
