using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Features.AddLiveSessionAttachment;
using Siri.Modules.Catalog.Features.DeleteLiveSessionAttachment;
using Siri.Modules.Catalog.Features.DownloadLiveSessionAttachment;
using Siri.Modules.Catalog.Features.GetLiveSessionAttachments;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

/// <summary>
/// Teaching materials (slides, handouts) attached to one live session (task P4-03c) — the live-class counterpart of
/// <see cref="EpisodeAttachmentsController"/>. Every action requires a signed-in user; reads additionally require
/// the caller to be the course's instructor, an admin, or a learner with an active enrollment in the session's course
/// (anyone else gets the same 404 as for a session that does not exist).
/// </summary>
[ApiController]
[Route("api/catalog/live-sessions/{sessionId:guid}/attachments")]
[Authorize]
[Tags("Catalog")]
public class LiveSessionAttachmentsController : ControllerBase
{
    [HttpGet("")]
    [EndpointName("GetLiveSessionAttachments")]
    [EndpointSummary("ดึงรายการไฟล์แนบของคาบสอนสด (ผู้สอน แอดมิน หรือผู้เรียนที่ลงทะเบียนคอร์ส)")]
    [ProducesResponseType(typeof(IReadOnlyList<LiveSessionAttachmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetLiveSessionAttachments(
        [FromRoute] Guid sessionId,
        [FromServices] GetLiveSessionAttachmentsHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(sessionId, userContext.UserId, IsAdmin(), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("{attachmentId:guid}/download")]
    [EndpointName("DownloadLiveSessionAttachment")]
    [EndpointSummary("ขอรับลิงก์ดาวน์โหลดไฟล์แนบของคาบสอนสด (ลิงก์ R2 อายุสั้น)")]
    [ProducesResponseType(typeof(LiveSessionAttachmentDownloadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> DownloadLiveSessionAttachment(
        [FromRoute] Guid sessionId,
        [FromRoute] Guid attachmentId,
        [FromServices] DownloadLiveSessionAttachmentHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(sessionId, attachmentId, userContext.UserId, IsAdmin(), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    /// <summary>
    /// Uploads one teaching-material file (multipart/form-data, part name <c>file</c>) to private Cloudflare R2.
    /// Instructor who owns the course, or an admin.
    /// </summary>
    [HttpPost("")]
    [EnableRateLimiting("default")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(EpisodeAttachmentOptions.MaxUploadRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = EpisodeAttachmentOptions.MaxUploadRequestBodyBytes)]
    [EndpointName("AddLiveSessionAttachment")]
    [EndpointSummary("อัปโหลดไฟล์แนบ (เอกสารประกอบการสอน) ให้คาบสอนสดไปที่ R2 (ผู้สอนเจ้าของคอร์ส หรือแอดมิน)")]
    [ProducesResponseType(typeof(LiveSessionAttachmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> AddLiveSessionAttachment(
        [FromRoute] Guid sessionId,
        IFormFile file,
        [FromServices] AddLiveSessionAttachmentHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        var command = new AddLiveSessionAttachmentCommand(file.FileName, file.ContentType, content);
        var result = await handler.HandleAsync(sessionId, command, userContext.UserId, IsAdmin(), cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/catalog/live-sessions/{sessionId}/attachments/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("{attachmentId:guid}")]
    [EnableRateLimiting("default")]
    [EndpointName("DeleteLiveSessionAttachment")]
    [EndpointSummary("ลบไฟล์แนบของคาบสอนสด (ลบไฟล์ใน R2 ด้วย)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> DeleteLiveSessionAttachment(
        [FromRoute] Guid sessionId,
        [FromRoute] Guid attachmentId,
        [FromServices] DeleteLiveSessionAttachmentHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(sessionId, attachmentId, userContext.UserId, IsAdmin(), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }

    private bool IsAdmin() =>
        HttpContext.User.IsInRole(RoleNames.Admin) || HttpContext.User.IsInRole(RoleNames.SuperAdmin);
}
