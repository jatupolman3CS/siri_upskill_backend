using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.SetCourseEnrollmentPolicy;

public static class SetCourseEnrollmentPolicyEndpoint
{
    public static IEndpointRouteBuilder MapSetCourseEnrollmentPolicyEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{id:guid}/enrollment-policy", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<SetCourseEnrollmentPolicyCommand>>()
            .WithName("CatalogSetCourseEnrollmentPolicy")
            .WithSummary("ตั้งนโยบายปิดรับสมัคร/เพดานที่นั่งของคอร์ส")
            .Produces<SetCourseEnrollmentPolicyResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        SetCourseEnrollmentPolicyCommand command,
        SetCourseEnrollmentPolicyHandler handler,
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
