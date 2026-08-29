using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.ApplyAsInstructor;
using Siri.Modules.Catalog.Features.ApproveInstructorApplication;
using Siri.Modules.Catalog.Features.GetMyInstructorProfile;
using Siri.Modules.Catalog.Features.GetPendingInstructorApplications;
using Siri.Modules.Catalog.Features.RejectInstructorApplication;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

[ApiController]
[Route("api/catalog")]
[Tags("Catalog")]
public class InstructorController : ControllerBase
{
    [HttpPost("instructors/apply")]
    [Authorize]
    [EndpointName("CatalogApplyAsInstructor")]
    [EndpointSummary("สมัครเป็นผู้สอน")]
    [ProducesResponseType(typeof(ApplyAsInstructorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> ApplyAsInstructor(
        [FromBody] ApplyAsInstructorCommand command,
        [FromServices] ApplyAsInstructorHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("instructors/me")]
    [Authorize]
    [EndpointName("CatalogGetMyInstructorProfile")]
    [EndpointSummary("ดูสถานะใบสมัคร/โปรไฟล์ผู้สอนของตัวเอง")]
    [ProducesResponseType(typeof(InstructorProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetMyInstructorProfile(
        [FromServices] GetMyInstructorProfileHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("admin/instructors/pending")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CatalogGetPendingInstructorApplications")]
    [EndpointSummary("รายการใบสมัครผู้สอนที่รออนุมัติ")]
    [ProducesResponseType(typeof(PagedResult<InstructorApplicationSummary>), StatusCodes.Status200OK)]
    public async Task<IResult> GetPendingInstructorApplications(
        [FromServices] GetPendingInstructorApplicationsHandler handler,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetPendingInstructorApplicationsHandler.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("admin/instructors/{id:guid}/approve")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CatalogApproveInstructorApplication")]
    [EndpointSummary("อนุมัติใบสมัครผู้สอน")]
    [ProducesResponseType(typeof(ApproveInstructorApplicationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> ApproveInstructorApplication(
        [FromRoute] Guid id,
        [FromServices] ApproveInstructorApplicationHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("admin/instructors/{id:guid}/reject")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CatalogRejectInstructorApplication")]
    [EndpointSummary("ปฏิเสธใบสมัครผู้สอน")]
    [ProducesResponseType(typeof(RejectInstructorApplicationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> RejectInstructorApplication(
        [FromRoute] Guid id,
        [FromServices] RejectInstructorApplicationHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
