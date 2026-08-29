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
[AllowAnonymous]
[Tags("Media Webhooks")]
public class BunnyWebhookController : ControllerBase
{
    [HttpPost("webhooks/bunny")]
    [EnableRateLimiting("webhook")]
    [EndpointName("MediaBunnyWebhook")]
    [EndpointSummary("Bunny Stream webhook receiver for video processing status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> HandleBunnyWebhook(
        [FromBody] BunnyWebhookPayload payload,
        [FromServices] BunnyWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        var authHeader = HttpContext.Request.Headers["Authorization"].FirstOrDefault();
        var result = await handler.HandleWebhookAsync(payload, authHeader, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(new { received = true })
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
