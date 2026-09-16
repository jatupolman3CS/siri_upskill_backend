using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.SetCourseDeliveryFormat;

public static class SetCourseDeliveryFormatEndpoint
{
    public static IEndpointRouteBuilder MapSetCourseDeliveryFormatEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{id:guid}/delivery-format", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<SetCourseDeliveryFormatCommand>>()
            .WithName("CatalogSetCourseDeliveryFormat")
            .WithSummary("เปลี่ยนรูปแบบการส่งมอบคอร์ส (OnDemand, Live, Hybrid)")
            .Produces<SetCourseDeliveryFormatResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        SetCourseDeliveryFormatCommand command,
        SetCourseDeliveryFormatHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
