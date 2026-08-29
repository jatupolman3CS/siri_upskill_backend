using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Admin.GetAdminAuditLogs;
using Siri.Modules.Identity.Features.Admin.GetAdminUsers;
using Siri.Modules.Identity.Features.Admin.InviteUser;
using Siri.Modules.Identity.Features.Admin.ReactivateUser;
using Siri.Modules.Identity.Features.Admin.SuspendUser;
using Siri.Modules.Identity.Features.Admin.UpdateUserRoles;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Identity;

[ApiController]
[Route("api/identity/admin")]
[Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
[Tags("Identity Admin")]
public class AdminUsersController : ControllerBase
{
    [HttpGet("users")]
    [EndpointName("GetAdminUsers")]
    [EndpointSummary("ค้นหาและกรองรายชื่อผู้ใช้สำหรับผู้ดูแลระบบ")]
    [ProducesResponseType(typeof(GetAdminUsersResult), StatusCodes.Status200OK)]
    public async Task<IResult> GetUsers(
        [FromQuery] string? query,
        [FromQuery] string? role,
        [FromQuery] UserStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromServices] GetAdminUsersHandler handler = default!,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(query, role, status, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("users/invite")]
    [EndpointName("InviteUser")]
    [EndpointSummary("เชิญผู้ใช้ใหม่เข้าสู่ระบบ")]
    [ProducesResponseType(typeof(InviteUserResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> InviteUser(
        [FromBody] InviteUserCommand command,
        [FromServices] InviteUserHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminId) return Results.Unauthorized();

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await handler.HandleAsync(adminId, command, ipAddress, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("users/{userId:guid}/suspend")]
    [EndpointName("SuspendUser")]
    [EndpointSummary("ระงับการใช้งานบัญชีผู้ใช้")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> SuspendUser(
        [FromRoute] Guid userId,
        [FromBody] SuspendUserCommand command,
        [FromServices] SuspendUserHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminId) return Results.Unauthorized();

        var result = await handler.HandleAsync(adminId, userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("users/{userId:guid}/reactivate")]
    [EndpointName("ReactivateUser")]
    [EndpointSummary("ยกเลิกการระงับและคืนสิทธิ์การใช้งานบัญชีผู้ใช้")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> ReactivateUser(
        [FromRoute] Guid userId,
        [FromServices] ReactivateUserHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminId) return Results.Unauthorized();

        var result = await handler.HandleAsync(adminId, userId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("users/{userId:guid}/roles")]
    [EndpointName("UpdateUserRoles")]
    [EndpointSummary("แก้ไขสิทธิ์และบทบาทของผู้ใช้")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> UpdateRoles(
        [FromRoute] Guid userId,
        [FromBody] UpdateUserRolesCommand command,
        [FromServices] UpdateUserRolesHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminId) return Results.Unauthorized();

        var result = await handler.HandleAsync(adminId, userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("audit-logs")]
    [EndpointName("GetAdminAuditLogs")]
    [EndpointSummary("ดูบันทึกเหตุการณ์ความปลอดภัยสำหรับผู้ดูแลระบบ")]
    [ProducesResponseType(typeof(GetAdminAuditLogsResult), StatusCodes.Status200OK)]
    public async Task<IResult> GetAuditLogs(
        [FromQuery] Guid? userId,
        [FromQuery] string? eventType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromServices] GetAdminAuditLogsHandler handler = default!,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(userId, eventType, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
