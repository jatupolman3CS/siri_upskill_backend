using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Siri.Api.Configuration;
using Siri.Modules.Live.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Live;

/// <summary>
/// The learner's side of live teaching (P11-05): their class list, their next classes, the <b>join gate</b> and a calendar file. Every action requires a signed-in user;
/// the user id always comes from <see cref="IUserContext"/>, never from the request. What a user may do with a session is decided by the services (an active, unexpired
/// enrollment of the session's course — or ownership of it — and nothing else; an administrator gets no bypass), and "no such session / not yours / enrollment over"
/// is the same 404 everywhere so nothing about who is entitled to what can be probed.
/// <para>
/// <b>The raw room link leaves the system only through <see cref="Join"/></b> (and the owning instructor's session detail); no other response here carries it. Every response is
/// <c>Cache-Control: no-store</c>. Each action has its own partitioned per-user rate limit — never the app-wide "default" window — so learners entering the room at the same
/// moment cannot starve each other.
/// </para>
/// </summary>
[ApiController]
[Route("api/live")]
[Authorize]
[Tags("Live")]
public class LiveLearnerController : ControllerBase
{
    [HttpGet("courses/{courseId:guid}/my-sessions")]
    [EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
    [EndpointName("LiveGetMySessions")]
    [EndpointSummary("ดูคาบเรียนสดของคอร์สที่ลงทะเบียนอยู่ (สถานะคำนวณที่ server — ไม่คืนลิงก์ห้อง)")]
    [ProducesResponseType(typeof(MySessionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetMySessions(
        [FromRoute] Guid courseId,
        [FromServices] LiveLearnerQueries queries,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        NoStore();

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await queries.GetMySessionsAsync(userId, courseId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("me/sessions/upcoming")]
    [EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
    [EndpointName("LiveGetMyUpcomingSessions")]
    [EndpointSummary("ดูคาบเรียนสดที่กำลังจะถึงของทุกคอร์สที่ลงทะเบียนอยู่ (ไม่คืนลิงก์ห้อง)")]
    [ProducesResponseType(typeof(MyUpcomingSessionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetMyUpcomingSessions(
        [FromServices] LiveLearnerQueries queries,
        [FromServices] IUserContext userContext,
        [FromQuery] int limit = LiveLearnerQueries.DefaultUpcomingLimit,
        CancellationToken cancellationToken = default)
    {
        NoStore();

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await queries.GetUpcomingAsync(userId, limit, cancellationToken).ConfigureAwait(false));
    }

    [HttpPost("sessions/{sessionId:guid}/join")]
    [EnableRateLimiting(RateLimiterConfiguration.LiveJoinPolicyName)]
    [EndpointName("LiveJoinSession")]
    [EndpointSummary("เข้าห้องเรียนสด — ตรวจสิทธิ์ที่ server แล้วจึงคืนลิงก์ห้อง (no-store)")]
    [ProducesResponseType(typeof(JoinLiveSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> Join(
        [FromRoute] Guid sessionId,
        [FromServices] SessionJoinService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        // Set before anything can fail: the headers must ride on the error responses too.
        NoStore();

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        // `sid` is the access token's session claim (forensics only); an unparseable/missing one is simply null.
        var authSessionId = Guid.TryParse(HttpContext.User.FindFirstValue("sid"), out var parsedSid) ? parsedSid : (Guid?)null;

        // RemoteIpAddress is the proxy's address until forwarded headers are configured — the same gap the playback log has.
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await service.JoinAsync(userId, sessionId, authSessionId, ipAddress, userAgent, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        if (result.Error.Reason == LiveReasons.MeetingNotReady)
        {
            Response.Headers.RetryAfter = SessionJoinService.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        return result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("sessions/{sessionId:guid}/calendar.ics")]
    [EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
    [EndpointName("LiveGetSessionCalendar")]
    [EndpointSummary("ดาวน์โหลดไฟล์ปฏิทิน (.ics) ของคาบเรียนสด — ลิงก์ในไฟล์ชี้กลับมาที่หน้าเข้าห้องของแพลตฟอร์ม ไม่ใช่ลิงก์ห้องจริง")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetSessionCalendar(
        [FromRoute] Guid sessionId,
        [FromServices] LiveLearnerQueries queries,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        NoStore();

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await queries.GetCalendarAsync(userId, sessionId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result.Error.ToProblemHttpResult(HttpContext);
        }

        // The header is written out by hand so it is exactly `attachment; filename="live-<id>.ics"` (the framework's own helper would drop the quotes).
        Response.Headers.ContentDisposition = $"attachment; filename=\"{result.Value.FileName}\"";

        return Results.Bytes(Encoding.UTF8.GetBytes(result.Value.Content), "text/calendar; charset=utf-8; method=PUBLISH");
    }

    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";
    }
}
