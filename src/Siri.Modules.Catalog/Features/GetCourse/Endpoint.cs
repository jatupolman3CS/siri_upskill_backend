using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetCourse;

/// <summary>Maps GET /api/catalog/instructor/courses/{id} (see <c>CatalogModule.MapCatalogEndpoints</c>
/// for the group prefix + InstructorOnly policy).</summary>
public static class GetCourseEndpoint
{
    public static IEndpointRouteBuilder MapGetCourseEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{id:guid}", HandleAsync)
            .WithName("CatalogGetCourse")
            .WithSummary("ดูรายละเอียดคอร์สของตัวเอง")
            .Produces<CourseResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        GetCourseHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
