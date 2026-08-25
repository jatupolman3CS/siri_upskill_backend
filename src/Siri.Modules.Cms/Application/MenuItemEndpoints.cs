using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Maps every <see cref="Domain.MENU_ITEM"/> HTTP endpoint — see <see cref="BannerEndpoints"/>'s own doc
/// comment for the shared file-per-entity/group-composed-by-caller shape and the "every handler is a stub
/// today" note.
/// </summary>
public static class MenuItemEndpoints
{
    /// <summary>Maps POST /api/cms/admin/menu-items.</summary>
    public static IEndpointRouteBuilder MapCreateMenuItemEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleCreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateMenuItemCommand>>()
            .WithName("CmsCreateMenuItem")
            .WithSummary("สร้างรายการเมนูใหม่")
            .Produces<MenuItemResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }

    /// <summary>Maps PUT /api/cms/admin/menu-items/{id}.</summary>
    public static IEndpointRouteBuilder MapUpdateMenuItemEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{id:guid}", HandleUpdateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UpdateMenuItemCommand>>()
            .WithName("CmsUpdateMenuItem")
            .WithSummary("แก้ไขรายการเมนู")
            .Produces<MenuItemResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps DELETE /api/cms/admin/menu-items/{id}.</summary>
    public static IEndpointRouteBuilder MapDeleteMenuItemEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/{id:guid}", HandleDeleteAsync)
            .WithName("CmsDeleteMenuItem")
            .WithSummary("ลบรายการเมนู (ต้องไม่มีเมนูย่อย)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    /// <summary>Maps GET /api/cms/admin/menu-items.</summary>
    public static IEndpointRouteBuilder MapListMenuItemsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListAsync)
            .WithName("CmsListMenuItems")
            .WithSummary("รายการเมนูทั้งหมด (แบบแบน ไม่แบ่งหน้า)")
            .Produces<IReadOnlyList<MenuItemResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreateMenuItemCommand command,
        MenuItemService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/cms/admin/menu-items/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleUpdateAsync(
        Guid id,
        UpdateMenuItemCommand command,
        MenuItemService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleDeleteAsync(
        Guid id,
        MenuItemService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleListAsync(
        MenuItemService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
