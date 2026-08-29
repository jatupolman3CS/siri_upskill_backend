using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Commerce.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Commerce;

[ApiController]
[Route("api/commerce")]
[Authorize]
[Tags("Commerce")]
public class RefundsController : ControllerBase
{
    [HttpPost("refunds")]
    [EndpointName("CommerceRequestRefund")]
    [EndpointSummary("ขอคืนเงินสำหรับการชำระเงินของตัวเอง")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> RequestRefund(
        [FromBody] RequestRefundCommand command,
        [FromServices] RefundService refundService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await refundService.RequestAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/refunds/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("refunds/{refundId:guid}")]
    [EndpointName("CommerceGetRefund")]
    [EndpointSummary("ดูสถานะคำขอคืนเงินของตัวเอง")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid refundId,
        [FromServices] RefundService refundService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await refundService.GetByIdAsync(userId, refundId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("admin/refunds/pending")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommerceListPendingRefunds")]
    [EndpointSummary("รายการคำขอคืนเงินที่รอการพิจารณา")]
    [ProducesResponseType(typeof(PagedResult<RefundResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> ListPending(
        [FromServices] RefundService refundService,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await refundService.ListPendingAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("admin/refunds/{refundId:guid}/approve")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommerceApproveRefund")]
    [EndpointSummary("อนุมัติคำขอคืนเงิน")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Approve(
        [FromRoute] Guid refundId,
        [FromBody] ApproveRefundCommand command,
        [FromServices] RefundService refundService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } decidedByUserId) return Results.Unauthorized();

        var result = await refundService.ApproveAsync(decidedByUserId, refundId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("admin/refunds/{refundId:guid}/reject")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommerceRejectRefund")]
    [EndpointSummary("ปฏิเสธคำขอคืนเงิน")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Reject(
        [FromRoute] Guid refundId,
        [FromBody] RejectRefundCommand command,
        [FromServices] RefundService refundService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } decidedByUserId) return Results.Unauthorized();

        var result = await refundService.RejectAsync(decidedByUserId, refundId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
