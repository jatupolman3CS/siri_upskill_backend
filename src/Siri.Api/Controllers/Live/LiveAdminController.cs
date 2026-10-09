using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Siri.Api.Configuration;
using Siri.Api.Diagnostics;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Live;

/// <summary>
/// Operator diagnostics for the live-course system: is a background job server running, are the recurring jobs scheduled, can e-mail leave, how is the
/// Live provider configured, what is queued or stuck. Administrators only (<see cref="AuthorizationPolicyNames.AdminOnly"/>); read-only; rate-limited per
/// user; <c>Cache-Control: no-store</c>. The response is aggregates and stable warning codes — never a secret, address, room URL, token or connection string.
/// </summary>
[ApiController]
[Route("api/live/admin")]
[Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
[EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
[Tags("Live")]
public class LiveAdminController : ControllerBase
{
    [HttpGet("status")]
    [EndpointName("LiveAdminGetStatus")]
    [EndpointSummary("สถานะระบบคอร์สสด: job server, recurring jobs, อีเมล, Google/Meet และคำเตือน (เฉพาะผู้ดูแล)")]
    [ProducesResponseType(typeof(LiveAdminStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetStatus(
        [FromServices] LiveAdminStatusService service,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var status = await service.GetStatusAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(status);
    }
}
