using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Commerce.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Commerce;

[ApiController]
[Route("api/commerce")]
[Tags("Commerce")]
public class BundlesController : ControllerBase
{
    [HttpGet("bundles/{bundleId:guid}")]
    [AllowAnonymous]
    [EndpointName("CommerceGetBundle")]
    [EndpointSummary("ดูรายละเอียดชุดคอร์ส")]
    [ProducesResponseType(typeof(BundleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid bundleId,
        [FromServices] BundleService bundleService,
        CancellationToken cancellationToken)
    {
        var result = await bundleService.GetByIdAsync(bundleId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("bundles")]
    [AllowAnonymous]
    [EndpointName("CommerceListBundles")]
    [EndpointSummary("รายการชุดคอร์สทั้งหมด")]
    [ProducesResponseType(typeof(PagedResult<BundleResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] BundleService bundleService,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await bundleService.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpGet("bundles/by-course/{courseId:guid}")]
    [AllowAnonymous]
    [EndpointName("CommerceGetBundlesByCourse")]
    [EndpointSummary("ดึงรายการชุดคอร์สที่เกี่ยวข้องกับคอร์สนี้ (Frequently Bought Together)")]
    [ProducesResponseType(typeof(IReadOnlyList<BundleResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetBundlesByCourse(
        [FromRoute] Guid courseId,
        [FromServices] BundleService bundleService,
        CancellationToken cancellationToken)
    {
        var result = await bundleService.GetBundlesByCourseIdAsync(courseId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("admin/bundles")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommerceCreateBundle")]
    [EndpointSummary("สร้างชุดคอร์สใหม่")]
    [ProducesResponseType(typeof(BundleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> Create(
        [FromBody] CreateBundleCommand command,
        [FromServices] BundleService bundleService,
        CancellationToken cancellationToken)
    {
        var result = await bundleService.CreateAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/bundles/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
