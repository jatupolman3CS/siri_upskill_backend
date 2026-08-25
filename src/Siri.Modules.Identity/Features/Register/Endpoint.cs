using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Register;

/// <summary>Maps POST /api/identity/register (see <c>IdentityModule.MapIdentityEndpoints</c> for the
/// "/api/identity" group prefix).</summary>
public static class RegisterEndpoint
{
    public static IEndpointRouteBuilder MapRegisterEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/register", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<RegisterCommand>>()
            // First real consumer of the "auth" named rate-limit policy (Program.cs) — registration
            // is exactly the kind of endpoint it exists for (backend.md: "Endpoint กลุ่มเสี่ยง (login,
            // refresh, playback token, checkout, webhook) ต้องมี rate limit policy ระบุชัด").
            .RequireRateLimiting("auth")
            // Explicitly public (P0-22): the "/api/identity" group now defaults to
            // RequireAuthorization() (IdentityModule.MapIdentityEndpoints), so this opts back out —
            // registering a new account is inherently something an unauthenticated caller must be
            // able to do.
            .AllowAnonymous()
            .WithName("IdentityRegister")
            .WithSummary("สมัครสมาชิกใหม่และส่งอีเมลยืนยันบัญชี")
            .Produces<RegisterResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        RegisterCommand command,
        RegisterHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
