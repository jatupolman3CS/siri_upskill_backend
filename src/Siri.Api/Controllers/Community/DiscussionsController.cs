using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Community.Application;
using Siri.Modules.Community.Application.Response;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Community;

[ApiController]
[Route("api/community/discussions")]
[Authorize]
[Tags("Community")]
public class DiscussionsController : ControllerBase
{
    [HttpPost("")]
    [EndpointName("CommunityCreateDiscussion")]
    [EndpointSummary("ตั้งกระทู้ใหม่หรือตอบกลับกระทู้เดิมในบทเรียน")]
    [ProducesResponseType(typeof(DiscussionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> Create(
        [FromBody] CreateDiscussionCommand command,
        [FromServices] DiscussionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/community/discussions/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("")]
    [EndpointName("CommunityListDiscussionsByEpisode")]
    [EndpointSummary("รายการถาม-ตอบของบทเรียนหนึ่งบท")]
    [ProducesResponseType(typeof(PagedResult<DiscussionResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> ListByEpisode(
        [FromQuery] Guid episodeId,
        [FromServices] DiscussionService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DiscussionService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListByEpisodeAsync(episodeId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/upvote")]
    [EndpointName("CommunityUpvoteDiscussion")]
    [EndpointSummary("โหวตว่ากระทู้นี้มีประโยชน์")]
    [ProducesResponseType(typeof(DiscussionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Upvote(
        [FromRoute] Guid id,
        [FromServices] DiscussionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.UpvoteAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("{id:guid}")]
    [EndpointName("CommunityDeleteDiscussion")]
    [EndpointSummary("ลบกระทู้ของตัวเอง")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Delete(
        [FromRoute] Guid id,
        [FromServices] DiscussionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.DeleteAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }
}
