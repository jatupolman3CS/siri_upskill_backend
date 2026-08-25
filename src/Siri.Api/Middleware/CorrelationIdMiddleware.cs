using Serilog.Context;

namespace Siri.Api.Middleware;

/// <summary>
/// Ensures every request carries a correlation id: reuses the caller's <c>X-Correlation-Id</c>
/// header when present, otherwise generates one. Echoes it back on the response and pushes it into
/// the Serilog log context so every log line for the request can be tied together
/// (ARCHITECTURE.md §2 "Observability" — "correlation id ทุก request").
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) &&
                             !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
