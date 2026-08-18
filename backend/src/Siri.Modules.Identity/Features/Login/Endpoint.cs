using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Identity.Infrastructure.Endpoints;

namespace Siri.Modules.Identity.Features.Login;

/// <summary>Maps POST /api/identity/login (see <c>IdentityModule.MapIdentityEndpoints</c> for the
/// "/api/identity" group prefix).</summary>
public static class LoginEndpoint
{
    public static IEndpointRouteBuilder MapLoginEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/login", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<LoginCommand>>()
            // Login is exactly the kind of endpoint the "auth" rate-limit policy exists for
            // (backend.md: "Endpoint กลุ่มเสี่ยง (login, refresh, ...) ต้องมี rate limit policy ระบุชัด")
            // — same named policy Register/ConfirmEmail already use.
            .RequireRateLimiting("auth")
            // Explicitly public (P0-22): logging in obviously cannot itself require being already
            // authenticated, so this opts back out of the "/api/identity" group's default
            // RequireAuthorization() (IdentityModule.MapIdentityEndpoints).
            .AllowAnonymous()
            .WithName("IdentityLogin")
            .WithSummary("เข้าสู่ระบบด้วยอีเมลและรหัสผ่าน")
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        LoginCommand command,
        LoginHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var result = await handler.HandleAsync(
            command,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            ipAddress,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblemHttpResult(httpContext);
        }

        // Refresh token: httpOnly/Secure/SameSite=Strict cookie only, never the JSON body (security.md).
        RefreshTokenCookie.Set(httpContext, result.Value.RawRefreshToken, result.Value.RefreshTokenExpiresAtUtc);

        return Results.Ok(new LoginResponse(result.Value.AccessToken, result.Value.AccessTokenExpiresAtUtc));
    }
}
