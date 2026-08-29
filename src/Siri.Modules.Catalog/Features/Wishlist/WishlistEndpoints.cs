using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.Wishlist;

public static class WishlistEndpoints
{
    public static IEndpointRouteBuilder MapWishlistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/wishlist").RequireAuthorization();

        group.MapGet("/", async (
            GetWishlistHandler handler,
            IUserContext userContext,
            CancellationToken cancellationToken) =>
        {
            if (!userContext.UserId.HasValue) return Results.Unauthorized();
            var result = await handler.HandleAsync(userContext.UserId.Value, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result.Value);
        })
        .WithName("GetWishlist")
        .WithSummary("ดึงรายการคอร์สที่บันทึกไว้ในสิ่งที่อยากได้ (Wishlist)")
        .Produces<IReadOnlyList<WishlistCourseItemResponse>>(StatusCodes.Status200OK);

        group.MapPost("/{courseId:guid}", async (
            Guid courseId,
            AddToWishlistHandler handler,
            IUserContext userContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!userContext.UserId.HasValue) return Results.Unauthorized();
            var result = await handler.HandleAsync(userContext.UserId.Value, courseId, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok() : result.Error.ToProblemHttpResult(httpContext);
        })
        .WithName("AddToWishlist")
        .WithSummary("เพิ่มคอร์สเข้าสู่สิ่งที่อยากได้")
        .Produces(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapDelete("/{courseId:guid}", async (
            Guid courseId,
            RemoveFromWishlistHandler handler,
            IUserContext userContext,
            CancellationToken cancellationToken) =>
        {
            if (!userContext.UserId.HasValue) return Results.Unauthorized();
            var result = await handler.HandleAsync(userContext.UserId.Value, courseId, cancellationToken).ConfigureAwait(false);
            return Results.Ok();
        })
        .WithName("RemoveFromWishlist")
        .WithSummary("นำคอร์สออกจากสิ่งที่อยากได้")
        .Produces(StatusCodes.Status200OK);

        return endpoints;
    }
}
