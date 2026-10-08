using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Siri.Api.Configuration;
using Siri.Modules.Live.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Live;

/// <summary>
/// The instructor's view of the online rooms behind their live sessions (P11-03): list a course's rooms, paste a link, retry. Instructors only
/// (<see cref="AuthorizationPolicyNames.InstructorOnly"/>), and <b>ownership is checked for every id by the service</b> — an administrator does not bypass it
/// (course moderation is a separate admin feature). The user id always comes from <see cref="IUserContext"/>.
/// <para>
/// No response carries a room URL — only <c>hasMeetingLink</c>. The URL is revealed solely by the join gate and the owning instructor's session detail.
/// All responses are <c>Cache-Control: no-store</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/live/instructor")]
[Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
[EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
[Tags("Live")]
public class LiveMeetingsController : ControllerBase
{
    [HttpGet("courses/{courseId:guid}/meetings")]
    [EndpointName("LiveGetCourseMeetings")]
    [EndpointSummary("ดูสถานะห้องประชุมของทุกคาบในคอร์ส (ไม่คืนลิงก์ห้อง)")]
    [ProducesResponseType(typeof(CourseMeetingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetCourseMeetings(
        [FromRoute] Guid courseId,
        [FromServices] SessionMeetingService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetCourseMeetingSummariesAsync(courseId, userId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("sessions/{sessionId:guid}/meeting-link")]
    [EndpointName("LiveSetManualMeetingLink")]
    [EndpointSummary("วางลิงก์ห้องประชุมเอง (Meet/Zoom/Teams) สำหรับคาบสอนสด — ไม่คืนลิงก์กลับ")]
    [ProducesResponseType(typeof(InstructorMeetingSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> SetMeetingLink(
        [FromRoute] Guid sessionId,
        [FromBody] SetMeetingLinkCommand command,
        [FromServices] SessionMeetingService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.SetManualLinkAsync(userId, sessionId, command.MeetUrl, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("sessions/{sessionId:guid}/meeting/resync")]
    [EndpointName("LiveResyncMeeting")]
    [EndpointSummary("ให้ระบบลองสร้างห้องประชุมของคาบนี้ใหม่")]
    [ProducesResponseType(typeof(InstructorMeetingSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> ResyncMeeting(
        [FromRoute] Guid sessionId,
        [FromServices] SessionMeetingService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ResyncAsync(userId, sessionId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
