using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Media.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Media;

[ApiController]
[Route("api/media/upload-sessions")]
[Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
[Tags("Media")]
public class MediaUploadSessionsController : ControllerBase
{
    [HttpGet("{id:guid}")]
    [EndpointName("MediaGetUploadSession")]
    [EndpointSummary("รายละเอียดเซสชันอัปโหลด")]
    [ProducesResponseType(typeof(MediaUploadSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid id,
        [FromServices] MediaUploadSessionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByIdAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/complete")]
    [EndpointName("MediaCompleteUploadSession")]
    [EndpointSummary("แจ้งว่าอัปโหลดเสร็จแล้ว")]
    [ProducesResponseType(typeof(MediaUploadSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Complete(
        [FromRoute] Guid id,
        [FromServices] MediaUploadSessionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CompleteAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

}
