using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Community.Application;
using Siri.Modules.Community.Application.Response;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Community;

[ApiController]
[Route("api/community/reports")]
[Authorize]
[Tags("Community")]
public class ReportsController : ControllerBase
{
    [HttpPost("")]
    [EndpointName("CommunityCreateReport")]
    [EndpointSummary("รายงานกระทู้ที่ไม่เหมาะสม")]
    [ProducesResponseType(typeof(ReportResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Create(
        [FromBody] CreateReportCommand command,
        [FromServices] ReportService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/community/reports/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("pending")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommunityListPendingReports")]
    [EndpointSummary("รายการรายงานที่รอตรวจสอบ")]
    [ProducesResponseType(typeof(PagedResult<ReportResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> ListPending(
        [FromServices] ReportService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = ReportService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListPendingAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommunityResolveReport")]
    [EndpointSummary("ทำเครื่องหมายว่ารายงานนี้ตรวจสอบแล้ว")]
    [ProducesResponseType(typeof(ReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Resolve(
        [FromRoute] Guid id,
        [FromServices] ReportService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ResolveAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/dismiss")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommunityDismissReport")]
    [EndpointSummary("ปิดรายงานโดยไม่พบปัญหา")]
    [ProducesResponseType(typeof(ReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Dismiss(
        [FromRoute] Guid id,
        [FromServices] ReportService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DismissAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
