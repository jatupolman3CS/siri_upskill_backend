using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Identity.Infrastructure.Endpoints;

namespace Siri.Modules.Identity.Features.Logout;

public static class LogoutEndpoint
{
    public static IEndpointRouteBuilder MapLogoutEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/logout", HandleAsync)
            .AllowAnonymous()
            .WithName("IdentityLogout")
            .WithSummary("ออกจากระบบและยกเลิกโทเคนเซสชัน")
            .Produces<LogoutResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        LogoutHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var rawRefreshToken = RefreshTokenCookie.Read(httpContext);
        await handler.HandleAsync(rawRefreshToken, cancellationToken).ConfigureAwait(false);
        RefreshTokenCookie.Clear(httpContext);

        return Results.Ok(new LogoutResponse("ออกจากระบบเรียบร้อยแล้ว"));
    }
}
