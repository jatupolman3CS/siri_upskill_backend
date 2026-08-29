using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.Wishlist;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

[ApiController]
[Route("api/catalog/wishlist")]
[Authorize]
[Tags("Catalog")]
public class WishlistController : ControllerBase
{
    [HttpGet("")]
    [EndpointName("GetWishlist")]
    [EndpointSummary("ดึงรายการคอร์สที่บันทึกไว้ในสิ่งที่อยากได้ (Wishlist)")]
    [ProducesResponseType(typeof(IReadOnlyList<WishlistCourseItemResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetWishlist(
        [FromServices] GetWishlistHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.UserId.HasValue) return Results.Unauthorized();
        var result = await handler.HandleAsync(userContext.UserId.Value, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result.Value);
    }

    [HttpPost("{courseId:guid}")]
    [EndpointName("AddToWishlist")]
    [EndpointSummary("เพิ่มคอร์สเข้าสู่สิ่งที่อยากได้")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> AddToWishlist(
        [FromRoute] Guid courseId,
        [FromServices] AddToWishlistHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.UserId.HasValue) return Results.Unauthorized();
        var result = await handler.HandleAsync(userContext.UserId.Value, courseId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok() : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("{courseId:guid}")]
    [EndpointName("RemoveFromWishlist")]
    [EndpointSummary("นำคอร์สออกจากสิ่งที่อยากได้")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IResult> RemoveFromWishlist(
        [FromRoute] Guid courseId,
        [FromServices] RemoveFromWishlistHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.UserId.HasValue) return Results.Unauthorized();
        var result = await handler.HandleAsync(userContext.UserId.Value, courseId, cancellationToken).ConfigureAwait(false);
        return Results.Ok();
    }
}
