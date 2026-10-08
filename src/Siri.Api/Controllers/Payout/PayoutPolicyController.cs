using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Siri.Api.Configuration;
using Siri.Modules.Payout.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Payout;

/// <summary>
/// The revenue/payout rules that apply to the signed-in user, so screens quote the real numbers (instructor share, platform share, withholding tax, minimum
/// payout, hold days) instead of hardcoding them. Any signed-in user may read it — the only per-user value is the revenue share, taken from the caller's own
/// instructor profile (default for everyone else); the user id always comes from <see cref="IUserContext"/>. Read-only: no money is calculated or changed here.
/// The response describes one person's rate, so it is <c>Cache-Control: no-store</c>; the endpoint has its own partitioned per-user rate limit
/// (<see cref="RateLimiterConfiguration.PayoutReadPolicyName"/>), never the app-wide "default" window.
/// </summary>
[ApiController]
[Route("api/payout/policy")]
[Authorize]
[EnableRateLimiting(RateLimiterConfiguration.PayoutReadPolicyName)]
[Tags("Payout Policy")]
public class PayoutPolicyController : ControllerBase
{
    [HttpGet]
    [EndpointName("PayoutGetPolicy")]
    [EndpointSummary("นโยบายส่วนแบ่งรายได้/การโอนเงินที่ใช้กับผู้ใช้ปัจจุบัน (ส่วนแบ่งผู้สอน, ภาษีหัก ณ ที่จ่าย, ขั้นต่ำ, วันพักเงิน) — no-store")]
    [ProducesResponseType(typeof(PayoutPolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetPolicy(
        [FromServices] PayoutPolicyService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetForUserAsync(userId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
