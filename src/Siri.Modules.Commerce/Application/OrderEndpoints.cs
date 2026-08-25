using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{orderId:guid}", GetByIdAsync)
            .WithName("CommerceGetOrder")
            .WithSummary("ดูรายละเอียดคำสั่งซื้อของตัวเอง")
            .Produces<OrderResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        // Route metadata (.WithName/.Produces) is what /openapi/v1.json is built from —
        // AddOpenApi()/MapOpenApi() read it at map-time and never invoke the handler, so the published
        // contract stays accurate independently of the handler body.
        endpoints.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateOrderCommand>>()
            .WithName("CommerceCreateOrder")
            .WithSummary("สร้างคำสั่งซื้อใหม่")
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetByIdAsync(Guid orderId, OrderService orderService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await orderService.GetByIdAsync(userId, orderId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> CreateAsync(CreateOrderCommand command, OrderService orderService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await orderService.CreateAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/orders/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
