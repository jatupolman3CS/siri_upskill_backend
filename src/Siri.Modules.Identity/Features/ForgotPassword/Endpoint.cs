using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.ForgotPassword;

/// <summary>Maps POST /api/identity/forgot-password (see <c>IdentityModule.MapIdentityEndpoints</c> for
/// the "/api/identity" group prefix).</summary>
public static class ForgotPasswordEndpoint
{
    public static IEndpointRouteBuilder MapForgotPasswordEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/forgot-password", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ForgotPasswordCommand>>()
            // Exactly the kind of endpoint the "auth" rate-limit policy exists for (backend.md) — an
            // unthrottled version of this endpoint would let an attacker both enumerate accounts by
            // timing/volume and mail-bomb an arbitrary inbox with reset links.
            .RequireRateLimiting("auth")
            // Explicitly public (P0-22): the "/api/identity" group defaults to RequireAuthorization()
            // (IdentityModule.MapIdentityEndpoints) — requesting a password reset is inherently
            // something an unauthenticated (indeed, possibly locked-out) caller must be able to do.
            .AllowAnonymous()
            .WithName("IdentityForgotPassword")
            .WithSummary("ขอลิงก์ตั้งรหัสผ่านใหม่ทางอีเมล")
            .Produces<ForgotPasswordResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        ForgotPasswordCommand command,
        ForgotPasswordHandler handler,
        CancellationToken cancellationToken)
    {
        // Always succeeds (see Handler.cs's doc comment) — no HttpContext/error-mapping needed, unlike
        // Register/Login/ConfirmEmail, which do have genuine failure branches to map.
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result.Value);
    }
}
