using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetMyCourses;

/// <summary>Maps GET /api/catalog/instructor/courses?page=&amp;pageSize= (see
/// <c>CatalogModule.MapCatalogEndpoints</c> for the group prefix + InstructorOnly policy).</summary>
public static class GetMyCoursesEndpoint
{
    public static IEndpointRouteBuilder MapGetMyCoursesEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleAsync)
            .WithName("CatalogGetMyCourses")
            .WithSummary("รายการคอร์สของตัวเอง")
            .Produces<PagedResult<CourseSummary>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        GetMyCoursesHandler handler,
        IUserContext userContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = GetMyCoursesHandler.DefaultPageSize)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
