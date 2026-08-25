using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetMyInstructorProfile;

/// <summary>Maps GET /api/catalog/instructors/me (see <c>CatalogModule.MapCatalogEndpoints</c> for the
/// group prefix). No <c>.AllowAnonymous()</c> — inherits the group's default
/// <c>.RequireAuthorization()</c>; checking your own application status requires being someone.</summary>
public static class GetMyInstructorProfileEndpoint
{
    public static IEndpointRouteBuilder MapGetMyInstructorProfileEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/instructors/me", HandleAsync)
            .WithName("CatalogGetMyInstructorProfile")
            .WithSummary("ดูสถานะใบสมัคร/โปรไฟล์ผู้สอนของตัวเอง")
            .Produces<InstructorProfileResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        GetMyInstructorProfileHandler handler,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
