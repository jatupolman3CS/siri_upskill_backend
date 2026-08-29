using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.AnonymizeAccount;

public static class AnonymizeAccountEndpoint
{
    public static IEndpointRouteBuilder MapAnonymizeAccountEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/anonymize", async (
            AnonymizeAccountRequest request,
            IUserContext userContext,
            HttpContext httpContext,
            AnonymizeAccountHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (userContext.UserId is null)
            {
                return Results.Unauthorized();
            }

            var command = new AnonymizeAccountCommand(userContext.UserId.Value, request.Password, request.Confirmation);
            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

            var result = await handler.HandleAsync(command, ipAddress, cancellationToken).ConfigureAwait(false);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error.ToProblemHttpResult(httpContext);
        })
        .AddEndpointFilter<ValidationEndpointFilter<AnonymizeAccountCommand>>()
        .WithName("AnonymizeAccount")
        .WithSummary("Anonymizes user account per PDPA Right to Erasure")
        .Produces<AnonymizeAccountResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
