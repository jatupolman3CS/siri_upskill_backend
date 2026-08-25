using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.ResetPassword;

/// <summary>Maps POST /api/identity/reset-password (see <c>IdentityModule.MapIdentityEndpoints</c> for
/// the "/api/identity" group prefix).</summary>
public static class ResetPasswordEndpoint
{
    public static IEndpointRouteBuilder MapResetPasswordEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/reset-password", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ResetPasswordCommand>>()
            // Token-guessing is exactly what rate limiting exists to slow down (same reasoning
            // ConfirmEmailEndpoint's own comment gives for its token) — same "auth" policy every other
            // Identity auth endpoint uses.
            .RequireRateLimiting("auth")
            // Explicitly public (P0-22): the "/api/identity" group defaults to RequireAuthorization()
            // (IdentityModule.MapIdentityEndpoints) — a caller redeeming a reset link is, by definition,
            // not authenticated yet (that is the entire point of this endpoint), so this opts back out.
            .AllowAnonymous()
            .WithName("IdentityResetPassword")
            .WithSummary("ตั้งรหัสผ่านใหม่ด้วยโทเคนจากลิงก์ตั้งรหัสผ่านใหม่")
            .Produces<ResetPasswordResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        ResetPasswordCommand command,
        ResetPasswordHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var result = await handler.HandleAsync(command, ipAddress, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
