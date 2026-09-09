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
        [FromServices] BunnyWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        return await BunnyWebhookRequest.HandleAsync(HttpContext, handler, cancellationToken).ConfigureAwait(false);
    }
}
