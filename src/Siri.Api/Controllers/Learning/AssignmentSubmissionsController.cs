using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Learning.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Learning;

[ApiController]
[Route("api/learning")]
[Tags("Learning")]
public class AssignmentSubmissionsController : ControllerBase
{
    [HttpPost("assignment-submissions")]
    [Authorize]
    [EndpointName("LearningSubmitAssignment")]
    [EndpointSummary("ส่งงานที่มอบหมาย")]
    [ProducesResponseType(typeof(AssignmentSubmissionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Submit(
        [FromBody] SubmitAssignmentRequest request,
        [FromServices] AssignmentSubmissionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.SubmitAsync(userId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/learning/assignment-submissions/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("assignment-submissions/{id:guid}")]
    [Authorize]
    [EndpointName("LearningGetAssignmentSubmission")]
    [EndpointSummary("ดูสถานะงานที่ส่ง")]
    [ProducesResponseType(typeof(AssignmentSubmissionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid id,
        [FromServices] AssignmentSubmissionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByIdAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("assignment-submissions/by-assignment/{assignmentId:guid}")]
    [Authorize]
    [EndpointName("LearningGetMyAssignmentSubmissionByAssignment")]
    [EndpointSummary("ดูงานที่ส่งล่าสุดของตัวเองในงานที่มอบหมายนี้")]
    [ProducesResponseType(typeof(AssignmentSubmissionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetMySubmissionByAssignment(
        [FromRoute] Guid assignmentId,
        [FromServices] AssignmentSubmissionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetMySubmissionByAssignmentAsync(userId, assignmentId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblemHttpResult(HttpContext);
        }

        return result.Value is not null ? Results.Ok(result.Value) : Results.NoContent();
    }

    [HttpGet("instructor/assignment-submissions/by-assignment/{assignmentId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningListAssignmentSubmissions")]
    [EndpointSummary("รายการงานที่ส่งเข้ามาของงานที่มอบหมายหนึ่งชิ้น")]
    [ProducesResponseType(typeof(PagedResult<AssignmentSubmissionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> ListByAssignment(
        [FromRoute] Guid assignmentId,
        [FromServices] AssignmentSubmissionService service,
        [FromServices] IUserContext userContext,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AssignmentSubmissionService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ListByAssignmentAsync(userId, assignmentId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("instructor/assignment-submissions/{id:guid}/grade")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningGradeAssignmentSubmission")]
    [EndpointSummary("ให้คะแนน/ตีกลับงานที่ส่ง")]
    [ProducesResponseType(typeof(AssignmentSubmissionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Grade(
        [FromRoute] Guid id,
        [FromBody] GradeAssignmentSubmissionRequest request,
        [FromServices] AssignmentSubmissionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GradeAsync(userId, id, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
