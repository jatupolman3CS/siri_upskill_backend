using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

public static class PlaybackSessionEndpoints
{
    public static IEndpointRouteBuilder MapPlaybackSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/playback-sessions", HandleCreatePlaybackSessionAsync)
            .RequireRateLimiting("default")
            .WithName("MediaCreatePlaybackSession")
            .WithSummary("ขอ Signed Playback URL สำหรับเล่นวิดีโอพร้อม Watermark")
            .Produces<PlaybackSessionResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> HandleCreatePlaybackSessionAsync(
        [FromBody] CreatePlaybackSessionCommand command,
        PlaybackSessionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var sessionIdStr = httpContext.User.FindFirstValue("sid");
        var sessionId = Guid.TryParse(sessionIdStr, out var parsedSid) ? parsedSid : Guid.Empty;
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var result = await service.CreateAsync(
            userId, sessionId, ipAddress, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
