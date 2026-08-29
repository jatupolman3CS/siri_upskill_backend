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
public class AssignmentsController : ControllerBase
{
    [HttpPost("instructor/assignments")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningCreateAssignment")]
    [EndpointSummary("สร้างงานที่มอบหมายใหม่สำหรับบทเรียน")]
    [ProducesResponseType(typeof(AssignmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IResult> Create(
        [FromBody] CreateAssignmentRequest request,
        [FromServices] AssignmentService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/learning/instructor/assignments/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("instructor/assignments/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningGetAssignment")]
    [EndpointSummary("ดูรายละเอียดงานที่มอบหมาย")]
    [ProducesResponseType(typeof(AssignmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid id,
        [FromServices] AssignmentService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("instructor/assignments/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningUpdateAssignment")]
    [EndpointSummary("แก้ไขงานที่มอบหมาย")]
    [ProducesResponseType(typeof(AssignmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateAssignmentRequest request,
        [FromServices] AssignmentService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.UpdateAsync(userId, id, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("assignments/by-episode/{episodeId:guid}")]
    [Authorize]
    [EndpointName("LearningGetAssignmentByEpisode")]
    [EndpointSummary("ดูรายละเอียดงานที่มอบหมายของบทเรียน (สำหรับผู้เรียน)")]
    [ProducesResponseType(typeof(AssignmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetByEpisodeForLearner(
        [FromRoute] Guid episodeId,
        [FromServices] AssignmentService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByEpisodeForLearnerAsync(userId, episodeId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
