using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.CreateLiveSession;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UpdateLiveSession;

public static class UpdateLiveSessionEndpoint
{
    public static IEndpointRouteBuilder MapUpdateLiveSessionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{courseId:guid}/live-sessions/{sessionId:guid}", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UpdateLiveSessionCommand>>()
            .WithName("CatalogUpdateLiveSession")
            .WithSummary("แก้ไขเวลาและข้อมูลคาบสอนสด")
            .Produces<LiveSessionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid courseId,
        Guid sessionId,
        UpdateLiveSessionCommand command,
        UpdateLiveSessionHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sessionId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
