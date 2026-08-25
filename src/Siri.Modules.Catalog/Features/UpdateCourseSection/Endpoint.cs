using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.CreateCourseSection;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UpdateCourseSection;

public static class UpdateCourseSectionEndpoint
{
    public static IEndpointRouteBuilder MapUpdateCourseSectionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{courseId:guid}/sections/{sectionId:guid}", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UpdateCourseSectionCommand>>()
            .WithName("CatalogUpdateCourseSection")
            .WithSummary("แก้ไขชื่อส่วน/บทหลักในคอร์ส")
            .Produces<CourseSectionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        Guid sectionId,
        UpdateCourseSectionCommand command,
        UpdateCourseSectionHandler handler,
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
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
