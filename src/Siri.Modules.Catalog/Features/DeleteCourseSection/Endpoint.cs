using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DeleteCourseSection;

public static class DeleteCourseSectionEndpoint
{
    public static IEndpointRouteBuilder MapDeleteCourseSectionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/{courseId:guid}/sections/{sectionId:guid}", HandleAsync)
            .WithName("CatalogDeleteCourseSection")
            .WithSummary("ลบส่วน/บทหลักออกจากคอร์ส")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        Guid sectionId,
        DeleteCourseSectionHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sectionId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
