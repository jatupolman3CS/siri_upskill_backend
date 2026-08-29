using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Media.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Media;

[ApiController]
[Route("api/media")]
[Tags("Media")]
public class PlaybackController : ControllerBase
{
    [HttpPost("playback-sessions")]
    [Authorize]
    [EnableRateLimiting("default")]
    [EndpointName("MediaCreatePlaybackSession")]
    [EndpointSummary("ขอ Signed Playback URL สำหรับเล่นวิดีโอพร้อม Watermark")]
    [ProducesResponseType(typeof(PlaybackSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> CreatePlaybackSession(
        [FromBody] CreatePlaybackSessionCommand command,
        [FromServices] PlaybackSessionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var sessionIdStr = HttpContext.User.FindFirstValue("sid");
        var sessionId = Guid.TryParse(sessionIdStr, out var parsedSid) ? parsedSid : Guid.Empty;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await service.CreateAsync(
            userContext.UserId, sessionId, ipAddress, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("playback/{episodeId:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting("default")]
    [EndpointName("MediaGetPlaybackSessionByEpisode")]
    [EndpointSummary("ขอข้อมูล Playback Session ตาม Episode ID (รองรับ Free Preview สำหรับผู้เยี่ยมชม)")]
    [ProducesResponseType(typeof(PlaybackSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetPlaybackByEpisode(
        [FromRoute] Guid episodeId,
        [FromQuery] string? deviceId,
        [FromServices] PlaybackSessionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var sessionIdStr = HttpContext.User.FindFirstValue("sid");
        var sessionId = Guid.TryParse(sessionIdStr, out var parsedSid) ? parsedSid : Guid.Empty;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await service.GetByEpisodeIdAsync(
            userContext.UserId, sessionId, ipAddress, deviceId, episodeId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
