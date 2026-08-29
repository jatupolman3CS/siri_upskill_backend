using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Media.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Media;

[ApiController]
[Route("api/media/assets")]
[Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
[Tags("Media")]
public class MediaAssetsController : ControllerBase
{
    [HttpPost("")]
    [EndpointName("MediaCreateAsset")]
    [EndpointSummary("ลงทะเบียนวิดีโอใหม่เพื่อเริ่มอัปโหลด")]
    [ProducesResponseType(typeof(MediaAssetResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> CreateAsset(
        [FromBody] CreateMediaAssetCommand command,
        [FromServices] MediaAssetService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/media/assets/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("")]
    [EndpointName("MediaGetMyAssets")]
    [EndpointSummary("รายการวิดีโอของตัวเอง")]
    [ProducesResponseType(typeof(PagedResult<MediaAssetResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetMyAssets(
        [FromServices] MediaAssetService service,
        [FromServices] IUserContext userContext,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MediaAssetService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetMyAssetsAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpGet("{id:guid}")]
    [EndpointName("MediaGetAsset")]
    [EndpointSummary("รายละเอียดวิดีโอ")]
    [ProducesResponseType(typeof(MediaAssetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetAsset(
        [FromRoute] Guid id,
        [FromServices] MediaAssetService service,
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

    [HttpDelete("{id:guid}")]
    [EndpointName("MediaDeleteAsset")]
    [EndpointSummary("ลบวิดีโอ")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> DeleteAsset(
        [FromRoute] Guid id,
        [FromServices] MediaAssetService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.DeleteAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{mediaAssetId:guid}/upload-sessions")]
    [EndpointName("MediaCreateUploadSession")]
    [EndpointSummary("เริ่มเซสชันอัปโหลดใหม่สำหรับวิดีโอนี้")]
    [ProducesResponseType(typeof(MediaUploadSessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> CreateUploadSession(
        [FromRoute] Guid mediaAssetId,
        [FromServices] MediaUploadSessionService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, mediaAssetId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/media/upload-sessions/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
