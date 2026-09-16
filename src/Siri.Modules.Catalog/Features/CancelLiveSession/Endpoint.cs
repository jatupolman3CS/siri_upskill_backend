using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.CreateLiveSession;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CancelLiveSession;

public static class CancelLiveSessionEndpoint
{
    public static IEndpointRouteBuilder MapCancelLiveSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/{courseId:guid}/live-sessions/{sessionId:guid}", HandleDeleteAsync)
            .WithName("CatalogDeleteLiveSession")
            .WithSummary("ยกเลิกคาบสอนสด (ไม่มีเหตุผล)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        endpoints.MapPost("/{courseId:guid}/live-sessions/{sessionId:guid}/cancel", HandleCancelWithReasonAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CancelLiveSessionCommand>>()
            .WithName("CatalogCancelLiveSessionWithReason")
            .WithSummary("ยกเลิกคาบสอนสดพร้อมระบุเหตุผล")
            .Produces<LiveSessionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleDeleteAsync(
        Guid courseId,
        Guid sessionId,
        CancelLiveSessionHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.CancelAsync(userId, courseId, sessionId, null, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleCancelWithReasonAsync(
        Guid courseId,
        Guid sessionId,
        CancelLiveSessionCommand command,
        CancelLiveSessionHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.CancelAsync(userId, courseId, sessionId, command.Reason, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
