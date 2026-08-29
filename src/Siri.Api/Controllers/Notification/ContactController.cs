using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Features.GetContactMessages;
using Siri.Modules.Notification.Features.ResolveContactMessage;
using Siri.Modules.Notification.Features.SubmitContactMessage;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Notification;

[ApiController]
[Tags("Contact")]
public class ContactController : ControllerBase
{
    [HttpPost("api/contact")]
    [AllowAnonymous]
    [EnableRateLimiting("default")]
    [EndpointName("SubmitContactMessage")]
    [EndpointSummary("ส่งข้อความติดต่อทีมงาน")]
    [ProducesResponseType(typeof(SubmitContactMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> SubmitContact(
        [FromBody] SubmitContactMessageCommand command,
        [FromServices] SubmitContactMessageHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("api/admin/contact-messages")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [Tags("Admin Contact")]
    [EndpointName("GetAdminContactMessages")]
    [EndpointSummary("ผู้ดูแลระบบดึงรายการข้อความติดต่อทั้งหมด")]
    [ProducesResponseType(typeof(PagedResult<ContactMessageListItemResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetAdminContactMessages(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] ContactMessageStatus? status,
        [FromServices] GetContactMessagesHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new GetContactMessagesQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize, status);
        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("api/admin/contact-messages/{id:guid}/resolve")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [Tags("Admin Contact")]
    [EndpointName("ResolveAdminContactMessage")]
    [EndpointSummary("ผู้ดูแลระบบทำเครื่องหมายว่าจัดการข้อความติดต่อแล้ว")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> ResolveAdminContactMessage(
        [FromRoute] Guid id,
        [FromBody] ResolveContactMessageCommand command,
        [FromServices] ResolveContactMessageHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminUserId) return Results.Unauthorized();

        var result = await handler.HandleAsync(id, adminUserId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(new { success = true })
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
