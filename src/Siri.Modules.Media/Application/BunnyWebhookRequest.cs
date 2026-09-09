using Microsoft.AspNetCore.Http;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

/// <summary>Shared HTTP binding for the MVC receiver and legacy module endpoint.</summary>
public static class BunnyWebhookRequest
{
    public static async Task<IResult> HandleAsync(
        HttpContext context, BunnyWebhookHandler handler, CancellationToken cancellationToken)
    {
        var buffer = new byte[BunnyWebhookHandler.MaximumBodyBytes + 1];
        var count = await context.Request.Body.ReadAtLeastAsync(
            buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        if (count > BunnyWebhookHandler.MaximumBodyBytes)
        {
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Webhook body is too large.");
        }

        var headers = context.Request.Headers;
        var result = await handler.HandleSignedWebhookAsync(buffer.AsMemory(0, count),
            headers["X-BunnyStream-Signature-Version"].ToString(),
            headers["X-BunnyStream-Signature-Algorithm"].ToString(),
            headers["X-BunnyStream-Signature"].ToString(), cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess) return Results.Ok(new { received = true });
        return result.Error.Code switch
        {
            BunnyWebhookHandler.InvalidSignature => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: result.Error.Message),
            BunnyWebhookHandler.ProviderUnavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: result.Error.Message),
            _ => result.Error.ToProblemHttpResult(context),
        };
    }
}
