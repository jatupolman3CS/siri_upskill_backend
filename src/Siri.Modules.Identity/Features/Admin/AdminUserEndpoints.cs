using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Admin.GetAdminAuditLogs;
using Siri.Modules.Identity.Features.Admin.GetAdminUsers;
using Siri.Modules.Identity.Features.Admin.ReactivateUser;
using Siri.Modules.Identity.Features.Admin.SuspendUser;
using Siri.Modules.Identity.Features.Admin.UpdateUserRoles;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Admin;

public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/identity/admin")
            .WithTags("Identity Admin")
            .RequireAuthorization(AuthorizationPolicyNames.AdminOnly);

        group.MapGet("/users", GetUsersAsync)
            .WithName("GetAdminUsers")
            .WithSummary("ค้นหาและกรองรายชื่อผู้ใช้สำหรับผู้ดูแลระบบ")
            .Produces<GetAdminUsersResult>(StatusCodes.Status200OK);

        group.MapPost("/users/{userId:guid}/suspend", SuspendUserAsync)
            .WithName("SuspendUser")
            .WithSummary("ระงับการใช้งานบัญชีผู้ใช้")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapPost("/users/{userId:guid}/reactivate", ReactivateUserAsync)
            .WithName("ReactivateUser")
            .WithSummary("ยกเลิกการระงับและคืนสิทธิ์การใช้งานบัญชีผู้ใช้")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapPut("/users/{userId:guid}/roles", UpdateRolesAsync)
            .WithName("UpdateUserRoles")
            .WithSummary("แก้ไขสิทธิ์และบทบาทของผู้ใช้")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        group.MapGet("/audit-logs", GetAuditLogsAsync)
            .WithName("GetAdminAuditLogs")
            .WithSummary("ดูบันทึกเหตุการณ์ความปลอดภัยสำหรับผู้ดูแลระบบ")
            .Produces<GetAdminAuditLogsResult>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> GetUsersAsync(
        [FromQuery] string? query,
        [FromQuery] string? role,
        [FromQuery] UserStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        GetAdminUsersHandler handler = default!,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(query, role, status, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> SuspendUserAsync(
        Guid userId,
        SuspendUserCommand command,
        SuspendUserHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminId) return Results.Unauthorized();

        var result = await handler.HandleAsync(adminId, userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ReactivateUserAsync(
        Guid userId,
        ReactivateUserHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminId) return Results.Unauthorized();

        var result = await handler.HandleAsync(adminId, userId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> UpdateRolesAsync(
        Guid userId,
        UpdateUserRolesCommand command,
        UpdateUserRolesHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminId) return Results.Unauthorized();

        var result = await handler.HandleAsync(adminId, userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetAuditLogsAsync(
        [FromQuery] Guid? userId,
        [FromQuery] string? eventType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        GetAdminAuditLogsHandler handler = default!,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(userId, eventType, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
