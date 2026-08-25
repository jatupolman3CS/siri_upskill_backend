using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.AutosaveCourse;

public static class AutosaveCourseEndpoint
{
    public static IEndpointRouteBuilder MapAutosaveCourseEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{courseId:guid}/autosave", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<AutosaveCourseCommand>>()
            .WithName("CatalogAutosaveCourse")
            .WithSummary("บันทึกอัตโนมัติ/อัปเดตข้อมูลโครงสร้างคอร์สทั้งหมด (Course Builder)")
            .Produces<AutosaveCourseResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        AutosaveCourseCommand command,
        AutosaveCourseHandler handler,
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
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
