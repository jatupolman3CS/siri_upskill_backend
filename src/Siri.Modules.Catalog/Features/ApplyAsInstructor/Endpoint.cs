using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.ApplyAsInstructor;

/// <summary>Maps POST /api/catalog/instructors/apply (see <c>CatalogModule.MapCatalogEndpoints</c> for
/// the group prefix). No <c>.AllowAnonymous()</c> — inherits the group's default
/// <c>.RequireAuthorization()</c>; applying to become an instructor requires being someone. 200 OK, not
/// 201 Created: this endpoint is conditionally create-or-resubmit (<c>InstructorProfile.Resubmit</c>
/// reuses an existing rejected row), so it is not always a pure creation.</summary>
public static class ApplyAsInstructorEndpoint
{
    public static IEndpointRouteBuilder MapApplyAsInstructorEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/instructors/apply", HandleAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ApplyAsInstructorCommand>>()
            .WithName("CatalogApplyAsInstructor")
            .WithSummary("สมัครเป็นผู้สอน")
            .Produces<ApplyAsInstructorResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        ApplyAsInstructorCommand command,
        ApplyAsInstructorHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // IUserContext.UserId only — never the request body (backend.md/security.md's IDOR-prevention
        // rule; see ApplyAsInstructorCommand's own doc comment).
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
