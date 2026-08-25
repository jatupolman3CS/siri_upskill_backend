using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Catalog.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetCourseDetail;

/// <summary>Maps GET /api/catalog/courses/{slug} (see <c>CatalogModule.MapCatalogEndpoints</c> for the
/// group prefix). Public — <c>.AllowAnonymous()</c>, same reasoning <c>SearchCoursesEndpoint</c>'s own
/// doc comment gives, including why <c>.CacheOutput(...)</c> (task P1-07) is safe here. The literal
/// <c>/courses/search</c> route always wins over this parameterized one for that exact path — ASP.NET
/// Core's routing scores literal segments over parameter segments regardless of registration order — so
/// there is no conflict between the two.</summary>
public static class GetCourseDetailEndpoint
{
    public static IEndpointRouteBuilder MapGetCourseDetailEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/courses/{slug}", HandleAsync)
            .AllowAnonymous()
            .CacheOutput(CourseOutputCache.PolicyName)
            .WithName("CatalogGetCourseDetail")
            .WithSummary("รายละเอียดคอร์สที่เผยแพร่แล้ว (สำหรับหน้า course detail สาธารณะ)")
            .Produces<CourseDetailResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        string slug,
        GetCourseDetailHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
