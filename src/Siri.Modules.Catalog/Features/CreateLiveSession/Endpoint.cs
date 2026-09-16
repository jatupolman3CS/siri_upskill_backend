using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CreateLiveSession;

public static class CreateLiveSessionEndpoint
{
    public static IEndpointRouteBuilder MapCreateLiveSessionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{courseId:guid}/live-sessions", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateLiveSessionCommand>>()
            .WithName("CatalogCreateLiveSession")
            .WithSummary("สร้างคาบสอนสดใหม่")
            .Produces<LiveSessionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        CreateLiveSessionCommand command,
        CreateLiveSessionHandler handler,
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
            ? Results.Created($"/api/catalog/instructor/courses/{courseId}/live-sessions/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
