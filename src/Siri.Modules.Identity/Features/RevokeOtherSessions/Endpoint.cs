using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Identity.Infrastructure.Endpoints;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.RevokeOtherSessions;

/// <summary>Maps POST /api/identity/sessions/revoke-others (see <c>IdentityModule.MapIdentityEndpoints</c>
/// for the "/api/identity" group prefix). POST, not DELETE — this is a bulk action against an implicit
/// collection ("every session but this one"), not the removal of one addressable resource (that is
/// <c>RevokeSessionEndpoint</c>'s job). No <c>.AllowAnonymous()</c> — inherits the group's default
/// <c>.RequireAuthorization()</c>.</summary>
public static class RevokeOtherSessionsEndpoint
{
    public static IEndpointRouteBuilder MapRevokeOtherSessionsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/sessions/revoke-others", HandleAsync)
            .WithName("IdentityRevokeOtherSessions")
            .WithSummary("ออกจากระบบในอุปกรณ์อื่นทั้งหมด (เก็บอุปกรณ์นี้ไว้)")
            .Produces<RevokeOtherSessionsResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        RevokeOtherSessionsHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var currentSessionId = CurrentSessionClaim.Read(httpContext.User);
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var response = await handler.HandleAsync(
            new RevokeOtherSessionsCommand(userId, currentSessionId), ipAddress, cancellationToken).ConfigureAwait(false);

        return Results.Ok(response);
    }
}
