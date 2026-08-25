using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.SubmitCourseForReview;

/// <summary>Maps POST /api/catalog/instructor/courses/{id}/submit-for-review (see
/// <c>CatalogModule.MapCatalogEndpoints</c> for the group prefix + InstructorOnly policy).</summary>
public static class SubmitCourseForReviewEndpoint
{
    public static IEndpointRouteBuilder MapSubmitCourseForReviewEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{id:guid}/submit-for-review", HandleAsync)
            .WithName("CatalogSubmitCourseForReview")
            .WithSummary("ส่งคอร์สเข้าตรวจสอบเพื่อเผยแพร่")
            .Produces<SubmitCourseForReviewResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        SubmitCourseForReviewHandler handler,
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
