using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.DataExport;

public static class DataExportEndpoint
{
    public static IEndpointRouteBuilder MapDataExportEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/data-export", async (
            IUserContext userContext,
            HttpContext httpContext,
            DataExportHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (userContext.UserId is null)
            {
                return Results.Unauthorized();
            }

            var command = new DataExportCommand(userContext.UserId.Value);
            var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error.ToProblemHttpResult(httpContext);
        })
        .WithName("DataExport")
        .WithSummary("Exports personal data for the authenticated user under PDPA")
        .Produces<DataExportResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
