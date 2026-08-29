using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.AddEpisodeAttachment;
using Siri.Modules.Catalog.Features.DeleteEpisodeAttachment;
using Siri.Modules.Catalog.Features.DownloadEpisodeAttachment;
using Siri.Modules.Catalog.Features.GetEpisodeAttachments;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

[ApiController]
[Route("api/catalog")]
[Tags("Catalog")]
public class EpisodeAttachmentsController : ControllerBase
{
    [HttpGet("episodes/{episodeId:guid}/attachments")]
    [AllowAnonymous]
    [EndpointName("GetEpisodeAttachments")]
    [EndpointSummary("ดึงรายการไฟล์แนบของบทเรียน (ตรวจสิทธิ์การลงทะเบียน/ตัวอย่างฟรี)")]
    [ProducesResponseType(typeof(IReadOnlyList<EpisodeAttachmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetEpisodeAttachments(
        [FromRoute] Guid episodeId,
        [FromServices] GetEpisodeAttachmentsHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var isAdmin = HttpContext.User.IsInRole(RoleNames.Admin) || HttpContext.User.IsInRole(RoleNames.SuperAdmin);
        var result = await handler.HandleAsync(episodeId, userContext.UserId, isAdmin, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("episodes/{episodeId:guid}/attachments/{attachmentId:guid}/download")]
    [AllowAnonymous]
    [EndpointName("DownloadEpisodeAttachment")]
    [EndpointSummary("ขอรับลิงก์ดาวน์โหลดไฟล์แนบของบทเรียน (ตรวจสิทธิ์การลงทะเบียน/ตัวอย่างฟรี)")]
    [ProducesResponseType(typeof(EpisodeAttachmentDownloadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> DownloadEpisodeAttachment(
        [FromRoute] Guid episodeId,
        [FromRoute] Guid attachmentId,
        [FromServices] DownloadEpisodeAttachmentHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var isAdmin = HttpContext.User.IsInRole(RoleNames.Admin) || HttpContext.User.IsInRole(RoleNames.SuperAdmin);
        var result = await handler.HandleAsync(episodeId, attachmentId, userContext.UserId, isAdmin, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("episodes/{episodeId:guid}/attachments")]
    [Authorize]
    [EndpointName("AddEpisodeAttachment")]
    [EndpointSummary("เพิ่มไฟล์แนบให้บทเรียน (ผู้สอนเจ้าของบทเรียน หรือแอดมิน)")]
    [ProducesResponseType(typeof(EpisodeAttachmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> AddEpisodeAttachment(
        [FromRoute] Guid episodeId,
        [FromBody] AddEpisodeAttachmentCommand command,
        [FromServices] AddEpisodeAttachmentHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var isAdmin = HttpContext.User.IsInRole(RoleNames.Admin) || HttpContext.User.IsInRole(RoleNames.SuperAdmin);
        var result = await handler.HandleAsync(episodeId, command, userContext.UserId, isAdmin, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/catalog/episodes/{episodeId}/attachments/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("episodes/{episodeId:guid}/attachments/{attachmentId:guid}")]
    [Authorize]
    [EndpointName("DeleteEpisodeAttachment")]
    [EndpointSummary("ลบไฟล์แนบของบทเรียน")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> DeleteEpisodeAttachment(
        [FromRoute] Guid episodeId,
        [FromRoute] Guid attachmentId,
        [FromServices] DeleteEpisodeAttachmentHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var isAdmin = HttpContext.User.IsInRole(RoleNames.Admin) || HttpContext.User.IsInRole(RoleNames.SuperAdmin);
        var result = await handler.HandleAsync(episodeId, attachmentId, userContext.UserId, isAdmin, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }
}
