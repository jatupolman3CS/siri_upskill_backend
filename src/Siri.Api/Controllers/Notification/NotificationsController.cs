using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Notification.Features.CreateAnnouncement;
using Siri.Modules.Notification.Features.GetCourseAnnouncements;
using Siri.Modules.Notification.Features.GetInstructorAnnouncements;
using Siri.Modules.Notification.Features.GetMyNotifications;
using Siri.Modules.Notification.Features.MarkNotificationRead;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Notification;

[ApiController]
[Route("api/notifications")]
[Authorize]
[Tags("Notifications")]
public class NotificationsController : ControllerBase
{
    [HttpPost("announcements")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("CreateCourseAnnouncement")]
    [EndpointSummary("ผู้สอนสร้างประกาศสำหรับคอร์สของตัวเอง")]
    [ProducesResponseType(typeof(AnnouncementResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IResult> CreateAnnouncement(
        [FromBody] CreateAnnouncementCommand command,
        [FromServices] CreateAnnouncementHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await handler.HandleAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/notifications/announcements/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("announcements/course/{courseId:guid}")]
    [EndpointName("GetCourseAnnouncements")]
    [EndpointSummary("ดึงรายการประกาศของคอร์ส")]
    [ProducesResponseType(typeof(IReadOnlyList<AnnouncementResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetCourseAnnouncements(
        [FromRoute] Guid courseId,
        [FromServices] GetCourseAnnouncementsHandler handler,
        CancellationToken cancellationToken)
    {
        var announcements = await handler.HandleAsync(courseId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(announcements);
    }

    [HttpGet("instructor/announcements")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("GetInstructorAnnouncements")]
    [EndpointSummary("ดึงรายการประกาศทั้งหมดของผู้สอน")]
    [ProducesResponseType(typeof(IReadOnlyList<AnnouncementResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetInstructorAnnouncements(
        [FromServices] GetInstructorAnnouncementsHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var announcements = await handler.HandleAsync(userId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(announcements);
    }

    [HttpGet("my")]
    [EndpointName("GetMyNotifications")]
    [EndpointSummary("ดึงรายการแจ้งเตือนของตัวเอง")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetMyNotifications(
        [FromServices] GetMyNotificationsHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var notifications = await handler.HandleAsync(userId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(notifications);
    }

    [HttpPost("{notificationId:guid}/read")]
    [EndpointName("MarkNotificationRead")]
    [EndpointSummary("ทำเครื่องหมายว่าอ่านการแจ้งเตือนแล้ว")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IResult> MarkRead(
        [FromRoute] Guid notificationId,
        [FromServices] MarkNotificationReadHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await handler.HandleAsync(userId, notificationId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
