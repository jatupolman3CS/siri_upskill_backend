using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Cms.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Cms;

[ApiController]
[Route("api/cms")]
[Tags("Cms")]
public class PostsController : ControllerBase
{
    [HttpGet("posts/{slug}")]
    [AllowAnonymous]
    [EndpointName("CmsGetPublishedPost")]
    [EndpointSummary("รายละเอียดบทความที่เผยแพร่แล้ว (สำหรับหน้า blog สาธารณะ)")]
    [ProducesResponseType(typeof(PostResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetPublishedBySlug(
        [FromRoute] string slug,
        [FromServices] PostService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetPublishedBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("posts")]
    [AllowAnonymous]
    [EndpointName("CmsListPublishedPosts")]
    [EndpointSummary("รายการบทความที่เผยแพร่แล้ว (สำหรับหน้า blog สาธารณะ)")]
    [ProducesResponseType(typeof(PagedResult<PostResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> ListPublished(
        [FromServices] PostService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListPublishedAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("admin/posts")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CmsCreatePost")]
    [EndpointSummary("สร้างบทความใหม่ (สถานะเริ่มต้น: ร่าง)")]
    [ProducesResponseType(typeof(PostResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Create(
        [FromBody] CreatePostCommand command,
        [FromServices] PostService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } authorUserId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(authorUserId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/cms/admin/posts/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("admin/posts/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CmsUpdatePost")]
    [EndpointSummary("แก้ไขเนื้อหาบทความ")]
    [ProducesResponseType(typeof(PostResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdatePostCommand command,
        [FromServices] PostService service,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("admin/posts/{id:guid}/status")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CmsChangePostStatus")]
    [EndpointSummary("เปลี่ยนสถานะบทความ (ร่าง/เผยแพร่/เก็บถาวร)")]
    [ProducesResponseType(typeof(PostResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> ChangeStatus(
        [FromRoute] Guid id,
        [FromBody] ChangePostStatusCommand command,
        [FromServices] PostService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ChangeStatusAsync(id, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("admin/posts/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CmsDeletePost")]
    [EndpointSummary("ลบบทความ (soft delete)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Delete(
        [FromRoute] Guid id,
        [FromServices] PostService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("admin/posts")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CmsListPosts")]
    [EndpointSummary("รายการบทความทั้งหมด (ทุกสถานะ)")]
    [ProducesResponseType(typeof(PagedResult<PostResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] PostService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
