using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UnpublishCourse;

public static class UnpublishCourseEndpoint
{
    public static IEndpointRouteBuilder MapUnpublishCourseEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{id:guid}/unpublish", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UnpublishCourseCommand>>()
            .WithName("CatalogUnpublishCourse")
            .WithSummary("ระงับหรือยกเลิกการเผยแพร่คอร์สเรียน (Admin)")
            .Produces<UnpublishCourseResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        UnpublishCourseCommand command,
        UnpublishCourseHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var adminUserId = userContext.UserId;
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var result = await handler.HandleAsync(id, adminUserId, ipAddress, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
