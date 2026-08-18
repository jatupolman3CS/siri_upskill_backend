using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Identity.Infrastructure.Endpoints;

namespace Siri.Modules.Identity.Features.Refresh;

/// <summary>Maps POST /api/identity/refresh (see <c>IdentityModule.MapIdentityEndpoints</c> for the
/// "/api/identity" group prefix). No request body — the refresh token travels only as the httpOnly
/// cookie <see cref="RefreshTokenCookie"/> reads/writes (task instruction: never a bindable
/// client-supplied JSON property), so unlike Register/ConfirmEmail/Login there is no
/// <c>RefreshCommand</c> bound as a minimal-API parameter and therefore no
/// <see cref="ValidationEndpointFilter{T}"/>/<c>IValidator&lt;RefreshCommand&gt;</c> to wire up either
/// — <see cref="RefreshCommand"/> is built by hand in <see cref="HandleAsync"/> below, and its one
/// real input-shape check (is the cookie present at all) is just a generic rejection inside the
/// handler, same as every other reason a refresh token can be invalid.</summary>
public static class RefreshEndpoint
{
    public static IEndpointRouteBuilder MapRefreshEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/refresh", HandleAsync)
            // Same "auth" rate-limit policy as Login/Register/ConfirmEmail (backend.md) — refresh is
            // explicitly called out in the task instructions as one of the risky endpoints this policy
            // exists for.
            .RequireRateLimiting("auth")
            // Explicitly public (P0-22), same reasoning as LoginEndpoint: refreshing a session cannot
            // itself require an already-valid access token (the whole point is to get a new one), so
            // this opts back out of the "/api/identity" group's default RequireAuthorization().
            .AllowAnonymous()
            .WithName("IdentityRefresh")
            .WithSummary("ขอ access token ใหม่ด้วย refresh token cookie พร้อม rotation")
            .Produces<RefreshResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        RefreshHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var rawRefreshToken = RefreshTokenCookie.Read(httpContext) ?? string.Empty;
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var command = new RefreshCommand(
            rawRefreshToken,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            ipAddress);

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblemHttpResult(httpContext);
        }

        RefreshTokenCookie.Set(httpContext, result.Value.RawRefreshToken, result.Value.RefreshTokenExpiresAtUtc);

        return Results.Ok(new RefreshResponse(result.Value.AccessToken, result.Value.AccessTokenExpiresAtUtc));
    }
}
