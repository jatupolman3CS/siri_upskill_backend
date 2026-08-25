using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>Endpoints for "a learner adds/removes cart items and views their cart" — see this scaffold
/// task's own instructions for CARTS/CART_ITEMS.</summary>
public static class CartEndpoints
{
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", GetMyCartAsync)
            .WithName("CommerceGetMyCart")
            .WithSummary("ดูตะกร้าสินค้าของตัวเอง")
            .Produces<CartResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapPost("/items", AddItemAsync)
            .AddEndpointFilter<ValidationEndpointFilter<AddCartItemCommand>>()
            .WithName("CommerceAddCartItem")
            .WithSummary("เพิ่มสินค้าลงตะกร้า")
            .Produces<CartResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        endpoints.MapDelete("/items/{cartItemId:guid}", RemoveItemAsync)
            .WithName("CommerceRemoveCartItem")
            .WithSummary("ลบสินค้าออกจากตะกร้า")
            .Produces<CartResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetMyCartAsync(CartService cartService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await cartService.GetMyCartAsync(userId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> AddItemAsync(AddCartItemCommand command, CartService cartService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await cartService.AddItemAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> RemoveItemAsync(Guid cartItemId, CartService cartService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await cartService.RemoveItemAsync(userId, cartItemId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
