using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Cms.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Cms;

[ApiController]
[Route("api/cms/admin/menu-items")]
[Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
[Tags("Cms Admin")]
public class MenuItemsController : ControllerBase
{
    [HttpPost("")]
    [EndpointName("CmsCreateMenuItem")]
    [EndpointSummary("สร้างรายการเมนูใหม่")]
    [ProducesResponseType(typeof(MenuItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> Create(
        [FromBody] CreateMenuItemCommand command,
        [FromServices] MenuItemService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/cms/admin/menu-items/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{id:guid}")]
    [EndpointName("CmsUpdateMenuItem")]
    [EndpointSummary("แก้ไขรายการเมนู")]
    [ProducesResponseType(typeof(MenuItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateMenuItemCommand command,
        [FromServices] MenuItemService service,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("{id:guid}")]
    [EndpointName("CmsDeleteMenuItem")]
    [EndpointSummary("ลบรายการเมนู (ต้องไม่มีเมนูย่อย)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Delete(
        [FromRoute] Guid id,
        [FromServices] MenuItemService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("")]
    [EndpointName("CmsListMenuItems")]
    [EndpointSummary("รายการเมนูทั้งหมด (แบบแบน ไม่แบ่งหน้า)")]
    [ProducesResponseType(typeof(IReadOnlyList<MenuItemResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] MenuItemService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
