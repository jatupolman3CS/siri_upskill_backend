using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

public static class StripeWebhookEndpoints
{
    public static IEndpointRouteBuilder MapStripeWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/stripe", HandleWebhookAsync)
            .AllowAnonymous()
            .RequireRateLimiting("webhook")
            .WithName("CommerceStripeWebhook")
            .WithSummary("รับ webhook event จาก Stripe")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpContext httpContext,
        StripeWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        var signatureHeader = httpContext.Request.Headers["Stripe-Signature"].FirstOrDefault();

        using var reader = new StreamReader(httpContext.Request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        var result = await handler.HandleAsync(json, signatureHeader, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(new { received = true, message = result.Value })
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
