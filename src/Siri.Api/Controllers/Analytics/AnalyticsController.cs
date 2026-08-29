using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Analytics.Features.AdminDashboardSummary;
using Siri.Modules.Analytics.Features.InstructorAnalytics;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Analytics;

[ApiController]
[Route("api/analytics")]
[Tags("Analytics")]
public class AnalyticsController : ControllerBase
{
    [HttpGet("admin/dashboard/summary")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("GetAdminDashboardSummary")]
    [EndpointSummary("ดึงข้อมูลสรุปแดชบอร์ดสำหรับผู้ดูแลระบบ")]
    [ProducesResponseType(typeof(AdminDashboardSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IResult> GetAdminDashboardSummary(
        [FromServices] GetAdminDashboardSummaryHandler handler,
        CancellationToken cancellationToken)
    {
        var summary = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(summary);
    }

    [HttpGet("instructor/dashboard/summary")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("GetInstructorAnalyticsSummary")]
    [EndpointSummary("ดึงข้อมูลสถิติและการวิเคราะห์สำหรับผู้สอน")]
    [ProducesResponseType(typeof(InstructorAnalyticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> GetInstructorAnalyticsSummary(
        [FromQuery] string? range,
        [FromQuery] Guid? courseId,
        [FromServices] GetInstructorAnalyticsHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userContext.UserId.Value, range, courseId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
