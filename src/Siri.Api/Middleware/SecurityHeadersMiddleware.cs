using Microsoft.AspNetCore.Http;

namespace Siri.Api.Middleware;

/// <summary>
/// Middleware enforcing OWASP recommended security headers across all responses (P7-02 / docs/SECURITY.md §5).
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["X-XSS-Protection"] = "0";

        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        }

        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "img-src 'self' data: https: blob:; " +
            "media-src 'self' https://*.b-cdn.net blob:; " +
            "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "font-src 'self' https://fonts.gstatic.com data:; " +
            "connect-src 'self' https://api.stripe.com https://*.bunnycdn.com https://*.b-cdn.net; " +
            "frame-ancestors 'none'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'";

        await next(context).ConfigureAwait(false);
    }
}
