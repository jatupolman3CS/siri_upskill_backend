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
public class BannersController : ControllerBase
{
    [HttpGet("banners")]
    [AllowAnonymous]
    [EndpointName("CmsListActiveBanners")]
    [EndpointSummary("รายการแบนเนอร์ที่เปิดใช้งานสำหรับผู้เข้าชมเว็บ")]
    [ProducesResponseType(typeof(IReadOnlyList<BannerResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> ListActive(
        [FromServices] BannerService service,
        [FromServices] IClock clock,
        [FromQuery] string placement = "HomeHero",
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetActiveByPlacementAsync(placement, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("admin/banners")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CmsCreateBanner")]
    [EndpointSummary("สร้างแบนเนอร์ใหม่")]
    [ProducesResponseType(typeof(BannerResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> Create(
        [FromBody] CreateBannerCommand command,
        [FromServices] BannerService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/cms/admin/banners/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("admin/banners/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CmsUpdateBanner")]
    [EndpointSummary("แก้ไขแบนเนอร์")]
    [ProducesResponseType(typeof(BannerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateBannerCommand command,
        [FromServices] BannerService service,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("admin/banners/reorder")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CmsReorderBanners")]
    [EndpointSummary("จัดลำดับแบนเนอร์ภายใน placement เดียวกันใหม่ทั้งหมด")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Reorder(
        [FromBody] ReorderBannersCommand command,
        [FromServices] BannerService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ReorderAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("admin/banners/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CmsDeleteBanner")]
    [EndpointSummary("ลบแบนเนอร์")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Delete(
        [FromRoute] Guid id,
        [FromServices] BannerService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("admin/banners")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CmsListBanners")]
    [EndpointSummary("รายการแบนเนอร์ทั้งหมด (ทุก placement)")]
    [ProducesResponseType(typeof(PagedResult<BannerResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] BannerService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
