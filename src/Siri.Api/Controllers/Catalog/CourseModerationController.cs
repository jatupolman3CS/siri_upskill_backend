using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.ApproveCourse;
using Siri.Modules.Catalog.Features.GetPendingCourseReviews;
using Siri.Modules.Catalog.Features.RejectCourse;
using Siri.Modules.Catalog.Features.UnpublishCourse;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

[ApiController]
[Route("api/catalog/admin/courses")]
[Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
[Tags("Catalog")]
public class CourseModerationController : ControllerBase
{
    [HttpGet("pending")]
    [EndpointName("CatalogGetPendingCourseReviews")]
    [EndpointSummary("รายการคอร์สที่รอตรวจสอบเพื่อเผยแพร่")]
    [ProducesResponseType(typeof(PagedResult<PendingCourseReviewSummary>), StatusCodes.Status200OK)]
    public async Task<IResult> GetPendingCourseReviews(
        [FromServices] GetPendingCourseReviewsHandler handler,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetPendingCourseReviewsHandler.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("{id:guid}/approve")]
    [EndpointName("CatalogApproveCourse")]
    [EndpointSummary("อนุมัติและเผยแพร่คอร์ส")]
    [ProducesResponseType(typeof(ApproveCourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> ApproveCourse(
        [FromRoute] Guid id,
        [FromServices] ApproveCourseHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/reject")]
    [EndpointName("CatalogRejectCourse")]
    [EndpointSummary("ปฏิเสธคอร์ส พร้อมเหตุผล")]
    [ProducesResponseType(typeof(RejectCourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> RejectCourse(
        [FromRoute] Guid id,
        [FromBody] RejectCourseCommand command,
        [FromServices] RejectCourseHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/unpublish")]
    [EndpointName("CatalogUnpublishCourse")]
    [EndpointSummary("ระงับหรือยกเลิกการเผยแพร่คอร์สเรียน (Admin)")]
    [ProducesResponseType(typeof(UnpublishCourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> UnpublishCourse(
        [FromRoute] Guid id,
        [FromBody] UnpublishCourseCommand command,
        [FromServices] UnpublishCourseHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var adminUserId = userContext.UserId;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await handler.HandleAsync(id, adminUserId, ipAddress, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
