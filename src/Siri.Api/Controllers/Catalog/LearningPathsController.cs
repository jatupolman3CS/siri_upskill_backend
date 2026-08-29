using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.CreateLearningPath;
using Siri.Modules.Catalog.Features.DeleteLearningPath;
using Siri.Modules.Catalog.Features.GetLearningPathBySlug;
using Siri.Modules.Catalog.Features.GetLearningPaths;
using Siri.Modules.Catalog.Features.UpdateLearningPath;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

[ApiController]
[Route("api/catalog")]
[Tags("Catalog")]
public class LearningPathsController : ControllerBase
{
    [HttpGet("learning-paths")]
    [AllowAnonymous]
    [EndpointName("GetPublicLearningPaths")]
    [EndpointSummary("ดึงรายการเส้นทางการเรียนที่เปิดใช้งาน")]
    [ProducesResponseType(typeof(IReadOnlyList<LearningPathSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetLearningPaths(
        [FromServices] GetLearningPathsHandler handler,
        CancellationToken cancellationToken)
    {
        var paths = await handler.HandleAsync(activeOnly: true, cancellationToken).ConfigureAwait(false);
        return Results.Ok(paths);
    }

    [HttpGet("learning-paths/{slug}")]
    [AllowAnonymous]
    [EndpointName("GetLearningPathBySlug")]
    [EndpointSummary("ดึงรายละเอียดเส้นทางการเรียนตาม slug")]
    [ProducesResponseType(typeof(LearningPathDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetLearningPathBySlug(
        [FromRoute] string slug,
        [FromServices] GetLearningPathBySlugHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("admin/learning-paths")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("GetAdminLearningPaths")]
    [EndpointSummary("ดึงรายการเส้นทางการเรียนทั้งหมด (แอดมิน)")]
    [ProducesResponseType(typeof(IReadOnlyList<LearningPathSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetAdminLearningPaths(
        [FromServices] GetLearningPathsHandler handler,
        CancellationToken cancellationToken)
    {
        var paths = await handler.HandleAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        return Results.Ok(paths);
    }

    [HttpPost("admin/learning-paths")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CreateLearningPath")]
    [EndpointSummary("สร้างเส้นทางการเรียนใหม่")]
    [ProducesResponseType(typeof(LearningPathDetailResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> CreateLearningPath(
        [FromBody] CreateLearningPathCommand command,
        [FromServices] CreateLearningPathHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/catalog/learning-paths/{result.Value.Slug}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("admin/learning-paths/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("UpdateLearningPath")]
    [EndpointSummary("แก้ไขเส้นทางการเรียน")]
    [ProducesResponseType(typeof(LearningPathDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> UpdateLearningPath(
        [FromRoute] Guid id,
        [FromBody] UpdateLearningPathCommand command,
        [FromServices] UpdateLearningPathHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("admin/learning-paths/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("DeleteLearningPath")]
    [EndpointSummary("ลบเส้นทางการเรียน")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> DeleteLearningPath(
        [FromRoute] Guid id,
        [FromServices] DeleteLearningPathHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }
}
