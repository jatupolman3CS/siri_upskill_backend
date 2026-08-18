using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.RevokeAllSessions;

/// <summary>Maps POST /api/identity/sessions/revoke-all (see <c>IdentityModule.MapIdentityEndpoints</c>
/// for the "/api/identity" group prefix). POST, not DELETE — a bulk action against an implicit
/// collection, same reasoning as <c>RevokeOtherSessionsEndpoint</c>. No <c>.AllowAnonymous()</c> —
/// inherits the group's default <c>.RequireAuthorization()</c>.</summary>
public static class RevokeAllSessionsEndpoint
{
    public static IEndpointRouteBuilder MapRevokeAllSessionsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/sessions/revoke-all", HandleAsync)
            .WithName("IdentityRevokeAllSessions")
            .WithSummary("ออกจากระบบในทุกอุปกรณ์ รวมถึงอุปกรณ์นี้ด้วย")
            .Produces<RevokeAllSessionsResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        RevokeAllSessionsHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var response = await handler.HandleAsync(new RevokeAllSessionsCommand(userId), ipAddress, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(response);
    }
}
