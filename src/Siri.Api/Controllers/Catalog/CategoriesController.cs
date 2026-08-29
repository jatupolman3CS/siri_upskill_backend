using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.CreateCategory;
using Siri.Modules.Catalog.Features.DeleteCategory;
using Siri.Modules.Catalog.Features.GetAdminCategoryTree;
using Siri.Modules.Catalog.Features.GetCategoryTree;
using Siri.Modules.Catalog.Features.ReorderCategories;
using Siri.Modules.Catalog.Features.UpdateCategory;
using Siri.Modules.Catalog.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

[ApiController]
[Route("api/catalog")]
[Tags("Catalog")]
public class CategoriesController : ControllerBase
{
    [HttpGet("categories")]
    [AllowAnonymous]
    [EndpointName("CatalogGetCategoryTree")]
    [EndpointSummary("รายการหมวดหมู่คอร์สทั้งหมด (เฉพาะที่เปิดใช้งาน) แบบโครงสร้างต้นไม้")]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryTreeNode>), StatusCodes.Status200OK)]
    public async Task<IResult> GetCategoryTree(
        [FromServices] GetCategoryTreeHandler handler,
        CancellationToken cancellationToken)
    {
        var json = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Content(json, "application/json");
    }

    [HttpGet("admin/categories")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CatalogGetAdminCategoryTree")]
    [EndpointSummary("รายการหมวดหมู่คอร์สทั้งหมด รวมที่ปิดใช้งานอยู่ สำหรับหน้าแอดมิน")]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryTreeNode>), StatusCodes.Status200OK)]
    public async Task<IResult> GetAdminCategoryTree(
        [FromServices] GetAdminCategoryTreeHandler handler,
        CancellationToken cancellationToken)
    {
        var tree = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(tree);
    }

    [HttpPost("admin/categories")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CatalogCreateCategory")]
    [EndpointSummary("สร้างหมวดหมู่คอร์สใหม่")]
    [ProducesResponseType(typeof(CreateCategoryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> CreateCategory(
        [FromBody] CreateCategoryCommand command,
        [FromServices] CreateCategoryHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/catalog/admin/categories/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("admin/categories/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CatalogUpdateCategory")]
    [EndpointSummary("แก้ไขหมวดหมู่คอร์ส")]
    [ProducesResponseType(typeof(UpdateCategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> UpdateCategory(
        [FromRoute] Guid id,
        [FromBody] UpdateCategoryCommand command,
        [FromServices] UpdateCategoryHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("admin/categories/reorder")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CatalogReorderCategories")]
    [EndpointSummary("จัดลำดับหมวดหมู่คอร์ส")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> ReorderCategories(
        [FromBody] ReorderCategoriesCommand command,
        [FromServices] ReorderCategoriesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok()
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("admin/categories/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CatalogDeleteCategory")]
    [EndpointSummary("ลบหมวดหมู่คอร์ส (เฉพาะที่ไม่มีคอร์สหรือหมวดหมู่ย่อย)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> DeleteCategory(
        [FromRoute] Guid id,
        [FromServices] DeleteCategoryHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
