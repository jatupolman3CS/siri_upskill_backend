using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Notification.Features.CreateAnnouncement;
using Siri.Modules.Notification.Features.GetCourseAnnouncements;
using Siri.Modules.Notification.Features.GetMyNotifications;
using Siri.Modules.Notification.Features.MarkNotificationRead;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Features;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        // Announcements (Instructor & Learner)
        group.MapPost("/announcements", CreateAnnouncementAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateAnnouncementCommand>>()
            .RequireAuthorization(AuthorizationPolicyNames.InstructorOnly)
            .WithName("CreateCourseAnnouncement")
            .WithSummary("ผู้สอนสร้างประกาศสำหรับคอร์สของตัวเอง")
            .Produces<AnnouncementResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        group.MapGet("/announcements/course/{courseId:guid}", GetCourseAnnouncementsAsync)
            .WithName("GetCourseAnnouncements")
            .WithSummary("ดึงรายการประกาศของคอร์ส")
            .Produces<IReadOnlyList<AnnouncementResponse>>(StatusCodes.Status200OK);

        group.MapGet("/instructor/announcements", GetInstructorAnnouncementsAsync)
            .RequireAuthorization(AuthorizationPolicyNames.InstructorOnly)
            .WithName("GetInstructorAnnouncements")
            .WithSummary("ดึงรายการประกาศทั้งหมดของผู้สอน")
            .Produces<IReadOnlyList<AnnouncementResponse>>(StatusCodes.Status200OK);

        // In-app Notifications (Learner)
        group.MapGet("/my", GetMyNotificationsAsync)
            .WithName("GetMyNotifications")
            .WithSummary("ดึงรายการแจ้งเตือนของตัวเอง")
            .Produces<IReadOnlyList<NotificationResponse>>(StatusCodes.Status200OK);

        group.MapPost("/{notificationId:guid}/read", MarkReadAsync)
            .WithName("MarkNotificationRead")
            .WithSummary("ทำเครื่องหมายว่าอ่านการแจ้งเตือนแล้ว")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        // Public Contact Form
        endpoints.MapPost("/api/contact", SubmitContactMessageAsync)
            .AllowAnonymous()
            .RequireRateLimiting("default")
            .WithTags("Contact")
            .WithName("SubmitContactMessage")
            .WithSummary("ส่งข้อความติดต่อทีมงาน")
            .Produces<SubmitContactMessage.SubmitContactMessageResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        // Admin Contact Messages
        var adminGroup = endpoints.MapGroup("/api/admin/contact-messages")
            .WithTags("Admin Contact")
            .RequireAuthorization(AuthorizationPolicyNames.AdminOnly);

        adminGroup.MapGet("", GetContactMessagesAsync)
            .WithName("GetAdminContactMessages")
            .WithSummary("ผู้ดูแลระบบดึงรายการข้อความติดต่อทั้งหมด")
            .Produces<PagedResult<GetContactMessages.ContactMessageListItemResponse>>(StatusCodes.Status200OK);

        adminGroup.MapPost("/{id:guid}/resolve", ResolveContactMessageAsync)
            .WithName("ResolveAdminContactMessage")
            .WithSummary("ผู้ดูแลระบบทำเครื่องหมายว่าจัดการข้อความติดต่อแล้ว")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> SubmitContactMessageAsync(
        SubmitContactMessage.SubmitContactMessageCommand command,
        SubmitContactMessage.SubmitContactMessageHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetContactMessagesAsync(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] Domain.ContactMessageStatus? status,
        GetContactMessages.GetContactMessagesHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new GetContactMessages.GetContactMessagesQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize, status);
        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ResolveContactMessageAsync(
        Guid id,
        ResolveContactMessage.ResolveContactMessageCommand command,
        ResolveContactMessage.ResolveContactMessageHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminUserId) return Results.Unauthorized();

        var result = await handler.HandleAsync(id, adminUserId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(new { success = true })
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetInstructorAnnouncementsAsync(
        GetInstructorAnnouncements.GetInstructorAnnouncementsHandler handler,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var announcements = await handler.HandleAsync(userId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(announcements);
    }

    private static async Task<IResult> CreateAnnouncementAsync(
        CreateAnnouncementCommand command,
        CreateAnnouncementHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await handler.HandleAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/notifications/announcements/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetCourseAnnouncementsAsync(
        Guid courseId,
        GetCourseAnnouncementsHandler handler,
        CancellationToken cancellationToken)
    {
        var announcements = await handler.HandleAsync(courseId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(announcements);
    }

    private static async Task<IResult> GetMyNotificationsAsync(
        GetMyNotificationsHandler handler,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var notifications = await handler.HandleAsync(userId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(notifications);
    }

    private static async Task<IResult> MarkReadAsync(
        Guid notificationId,
        MarkNotificationReadHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await handler.HandleAsync(userId, notificationId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
