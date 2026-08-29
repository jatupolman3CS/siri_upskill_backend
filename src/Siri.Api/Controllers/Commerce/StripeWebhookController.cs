using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Commerce.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Commerce;

[ApiController]
[Route("api/commerce/webhooks")]
[AllowAnonymous]
[Tags("Commerce Webhooks")]
public class StripeWebhookController : ControllerBase
{
    [HttpPost("stripe")]
    [EnableRateLimiting("webhook")]
    [EndpointName("CommerceStripeWebhook")]
    [EndpointSummary("รับ webhook event จาก Stripe")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> HandleWebhook(
        [FromServices] StripeWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        var signatureHeader = HttpContext.Request.Headers["Stripe-Signature"].FirstOrDefault();

        using var reader = new StreamReader(HttpContext.Request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        var result = await handler.HandleAsync(json, signatureHeader, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(new { received = true, message = result.Value })
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
