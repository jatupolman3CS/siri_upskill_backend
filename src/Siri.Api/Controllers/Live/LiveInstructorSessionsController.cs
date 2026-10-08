using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Siri.Api.Configuration;
using Siri.Modules.Live.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Live;

/// <summary>
/// The instructor's view of their own live sessions (P11-05): a list, one session's detail and a roster. Instructors only
/// (<see cref="AuthorizationPolicyNames.InstructorOnly"/>), and <b>ownership is checked for every id by the service</b> — an administrator does not bypass it. The user id always
/// comes from <see cref="IUserContext"/>. A session or course is not a secret from its own instructor, so these endpoints tell "not yours" (403) from "does not exist" (404).
/// <para>
/// The session detail is one of the only two places the raw room link leaves the system (the other is the learner join gate); it and every other response here is
/// <c>Cache-Control: no-store</c>. A roster shows only a masked e-mail address. All actions use the partitioned per-user <c>live-user</c> rate limit.
/// </para>
/// </summary>
[ApiController]
[Route("api/live/instructor/sessions")]
[Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
[EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
[Tags("Live")]
public class LiveInstructorSessionsController : ControllerBase
{
    [HttpGet("")]
    [EndpointName("LiveGetInstructorSessions")]
    [EndpointSummary("รายการคาบสอนสดของผู้สอน (upcoming/past/all, กรองตามคอร์สได้) พร้อมสถานะห้องและจำนวนผู้เข้าร่วม — ไม่คืนลิงก์ห้อง")]
    [ProducesResponseType(typeof(PagedResult<InstructorSessionListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetSessions(
        [FromServices] LiveInstructorQueries queries,
        [FromServices] IUserContext userContext,
        [FromQuery] InstructorSessionScope scope = InstructorSessionScope.Upcoming,
        [FromQuery] Guid? courseId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = LiveInstructorQueries.DefaultSessionPageSize,
        CancellationToken cancellationToken = default)
    {
        NoStore();

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await queries.GetSessionsAsync(userId, scope, courseId, page, pageSize, cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("{sessionId:guid}")]
    [EndpointName("LiveGetInstructorSession")]
    [EndpointSummary("รายละเอียดคาบสอนสดของผู้สอน รวมลิงก์ห้อง (เฉพาะเจ้าของคาบ, no-store)")]
    [ProducesResponseType(typeof(InstructorSessionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetSession(
        [FromRoute] Guid sessionId,
        [FromServices] LiveInstructorQueries queries,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        NoStore();

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await queries.GetSessionDetailAsync(userId, sessionId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("{sessionId:guid}/roster")]
    [EndpointName("LiveGetInstructorSessionRoster")]
    [EndpointSummary("รายชื่อผู้เรียนของคาบสอนสด (กรอง joined/notJoined) — อีเมลถูกปิดบังบางส่วน")]
    [ProducesResponseType(typeof(PagedResult<RosterItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetRoster(
        [FromRoute] Guid sessionId,
        [FromServices] LiveInstructorQueries queries,
        [FromServices] IUserContext userContext,
        [FromQuery] RosterFilter filter = RosterFilter.All,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = LiveInstructorQueries.DefaultRosterPageSize,
        CancellationToken cancellationToken = default)
    {
        NoStore();

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await queries.GetRosterAsync(userId, sessionId, filter, page, pageSize, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";
    }
}
