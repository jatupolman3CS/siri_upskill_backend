using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

public static class BunnyWebhookEndpoints
{
    public static IEndpointRouteBuilder MapBunnyWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/bunny", HandleBunnyWebhookAsync)
            .AllowAnonymous()
            .RequireRateLimiting("webhook")
            .WithName("MediaBunnyWebhook")
            .WithSummary("Bunny Stream webhook receiver for video processing status")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleBunnyWebhookAsync(
        BunnyWebhookHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return await BunnyWebhookRequest.HandleAsync(httpContext, handler, cancellationToken).ConfigureAwait(false);
    }
}
