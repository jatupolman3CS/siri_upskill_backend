using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Identity.Features.ListSessions;
using Siri.Modules.Identity.Features.RevokeAllSessions;
using Siri.Modules.Identity.Features.RevokeOtherSessions;
using Siri.Modules.Identity.Features.RevokeSession;
using Siri.Modules.Identity.Infrastructure.Endpoints;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Identity;

[ApiController]
[Route("api/identity/sessions")]
[Authorize]
[Tags("Identity")]
public class SessionsController : ControllerBase
{
    [HttpGet("")]
    [EndpointName("IdentityListSessions")]
    [EndpointSummary("แสดงรายการอุปกรณ์ที่เข้าสู่ระบบของบัญชีตัวเอง")]
    [ProducesResponseType(typeof(ListSessionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> ListSessions(
        [FromServices] ListSessionsHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var currentSessionId = CurrentSessionClaim.Read(HttpContext.User);

        var response = await handler.HandleAsync(new ListSessionsCommand(userId, currentSessionId), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(response);
    }

    [HttpDelete("{sessionId:guid}")]
    [EndpointName("IdentityRevokeSession")]
    [EndpointSummary("ถอดอุปกรณ์หนึ่งเครื่องออกจากระบบ (เฉพาะอุปกรณ์ของตัวเองเท่านั้น)")]
    [ProducesResponseType(typeof(RevokeSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> RevokeSession(
        [FromRoute] Guid sessionId,
        [FromServices] RevokeSessionHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var currentSessionId = CurrentSessionClaim.Read(HttpContext.User);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var command = new RevokeSessionCommand(userId, sessionId, currentSessionId);
        var result = await handler.HandleAsync(command, ipAddress, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("revoke-others")]
    [EndpointName("IdentityRevokeOtherSessions")]
    [EndpointSummary("ออกจากระบบในอุปกรณ์อื่นทั้งหมด (เก็บอุปกรณ์นี้ไว้)")]
    [ProducesResponseType(typeof(RevokeOtherSessionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> RevokeOtherSessions(
        [FromServices] RevokeOtherSessionsHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var currentSessionId = CurrentSessionClaim.Read(HttpContext.User);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var response = await handler.HandleAsync(
            new RevokeOtherSessionsCommand(userId, currentSessionId), ipAddress, cancellationToken).ConfigureAwait(false);

        return Results.Ok(response);
    }

    [HttpPost("revoke-all")]
    [EndpointName("IdentityRevokeAllSessions")]
    [EndpointSummary("ออกจากระบบในทุกอุปกรณ์ รวมถึงอุปกรณ์นี้ด้วย")]
    [ProducesResponseType(typeof(RevokeAllSessionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> RevokeAllSessions(
        [FromServices] RevokeAllSessionsHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var response = await handler.HandleAsync(new RevokeAllSessionsCommand(userId), ipAddress, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(response);
    }
}
