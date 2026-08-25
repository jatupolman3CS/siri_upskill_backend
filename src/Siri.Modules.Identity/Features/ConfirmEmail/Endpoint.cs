using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.ConfirmEmail;

/// <summary>Maps POST /api/identity/confirm-email (see <c>IdentityModule.MapIdentityEndpoints</c> for
/// the "/api/identity" group prefix).</summary>
public static class ConfirmEmailEndpoint
{
    public static IEndpointRouteBuilder MapConfirmEmailEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/confirm-email", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ConfirmEmailCommand>>()
            // Token-guessing is exactly what rate limiting exists to slow down (task instruction) —
            // same "auth" policy Register uses.
            .RequireRateLimiting("auth")
            // Explicitly public (P0-22) — same reasoning as RegisterEndpoint: confirming an email
            // address is inherently something an unauthenticated caller must be able to do, so this
            // opts back out of the "/api/identity" group's default RequireAuthorization().
            .AllowAnonymous()
            .WithName("IdentityConfirmEmail")
            .WithSummary("ยืนยันอีเมลด้วยโทเคนจากลิงก์ยืนยัน")
            .Produces<ConfirmEmailResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        ConfirmEmailCommand command,
        ConfirmEmailHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
