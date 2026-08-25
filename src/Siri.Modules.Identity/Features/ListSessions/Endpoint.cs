using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Identity.Infrastructure.Endpoints;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.ListSessions;

/// <summary>Maps GET /api/identity/sessions (see <c>IdentityModule.MapIdentityEndpoints</c> for the
/// "/api/identity" group prefix). No <c>.AllowAnonymous()</c> here — deliberately inherits the group's
/// default <c>.RequireAuthorization()</c> (task instruction: managing your own devices requires being
/// someone, unlike Register/Login/Refresh/ForgotPassword/ResetPassword).</summary>
public static class ListSessionsEndpoint
{
    public static IEndpointRouteBuilder MapListSessionsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/sessions", HandleAsync)
            .WithName("IdentityListSessions")
            .WithSummary("แสดงรายการอุปกรณ์ที่เข้าสู่ระบบของบัญชีตัวเอง")
            .Produces<ListSessionsResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        ListSessionsHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // IUserContext.UserId is the ONLY legitimate source for "whose sessions" — never a client-
        // supplied query/route/body value (see ListSessionsCommand's own doc comment; this is this
        // task's headline IDOR-prevention point applied to the read side). Defensive-only: the group's
        // RequireAuthorization() already guarantees an authenticated principal reaches here, and every
        // access token this codebase issues always carries ClaimTypes.NameIdentifier
        // (AccessTokenGenerator), so UserId should never actually be null here — same "should be
        // unreachable in practice, checked anyway" posture Refresh/ConfirmEmail's handlers already take
        // for their own "should never happen" branches.
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var currentSessionId = CurrentSessionClaim.Read(httpContext.User);

        var response = await handler.HandleAsync(new ListSessionsCommand(userId, currentSessionId), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(response);
    }
}
