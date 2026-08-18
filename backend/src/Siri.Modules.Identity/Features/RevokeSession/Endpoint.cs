using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Identity.Infrastructure.Endpoints;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.RevokeSession;

/// <summary>Maps DELETE /api/identity/sessions/{sessionId} (see <c>IdentityModule.MapIdentityEndpoints</c>
/// for the "/api/identity" group prefix). DELETE, not POST — this removes one specific, addressable
/// resource (a session), which is exactly what DELETE is for; <c>RevokeOtherSessionsEndpoint</c>/
/// <c>RevokeAllSessionsEndpoint</c> use POST instead because those are bulk actions on an implicit
/// collection, not a single addressable resource. No <c>.AllowAnonymous()</c> — inherits the group's
/// default <c>.RequireAuthorization()</c>.</summary>
public static class RevokeSessionEndpoint
{
    public static IEndpointRouteBuilder MapRevokeSessionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/sessions/{sessionId:guid}", HandleAsync)
            .WithName("IdentityRevokeSession")
            .WithSummary("ถอดอุปกรณ์หนึ่งเครื่องออกจากระบบ (เฉพาะอุปกรณ์ของตัวเองเท่านั้น)")
            .Produces<RevokeSessionResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid sessionId,
        RevokeSessionHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // IUserContext.UserId only — see RevokeSessionCommand's doc comment for why this must never
        // come from the route/body/query instead (this task's headline IDOR-prevention point).
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var currentSessionId = CurrentSessionClaim.Read(httpContext.User);
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var command = new RevokeSessionCommand(userId, sessionId, currentSessionId);
        var result = await handler.HandleAsync(command, ipAddress, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
