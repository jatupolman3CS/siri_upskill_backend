using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Learning.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Learning;

[ApiController]
[Route("api/learning/enrollments/{enrollmentId:guid}/episode-progress")]
[Authorize]
[Tags("Learning")]
public class EpisodeProgressController : ControllerBase
{
    [HttpPut("{episodeId:guid}")]
    [EndpointName("LearningUpsertEpisodeProgress")]
    [EndpointSummary("บันทึกความคืบหน้าการดูวิดีโอของบทเรียน (heartbeat)")]
    [ProducesResponseType(typeof(EpisodeProgressResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Upsert(
        [FromRoute] Guid enrollmentId,
        [FromRoute] Guid episodeId,
        [FromBody] UpsertEpisodeProgressCommand command,
        [FromServices] EpisodeProgressService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.UpsertProgressAsync(userId, enrollmentId, episodeId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("")]
    [EndpointName("LearningListEpisodeProgressForEnrollment")]
    [EndpointSummary("ดูความคืบหน้าทุกบทเรียนของการลงทะเบียนหนึ่งรายการ")]
    [ProducesResponseType(typeof(IReadOnlyList<EpisodeProgressResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> ListForEnrollment(
        [FromRoute] Guid enrollmentId,
        [FromServices] EpisodeProgressService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetForEnrollmentAsync(userId, enrollmentId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
