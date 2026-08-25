using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Maps every <see cref="Domain.REDIRECT"/> HTTP endpoint — see <see cref="BannerEndpoints"/>'s own doc
/// comment for the shared file-per-entity/group-composed-by-caller shape and the "every handler is a stub
/// today" note.
/// </summary>
public static class RedirectEndpoints
{
    /// <summary>Maps POST /api/cms/admin/redirects.</summary>
    public static IEndpointRouteBuilder MapCreateRedirectEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleCreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateRedirectCommand>>()
            .WithName("CmsCreateRedirect")
            .WithSummary("สร้างกฎ redirect ใหม่")
            .Produces<RedirectResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    /// <summary>Maps GET /api/cms/admin/redirects/lookup?fromPath=. A query parameter, not a
    /// <c>/redirects/{fromPath}</c> route segment — <see cref="Domain.REDIRECT.FROM_PATH"/> is itself a
    /// path that can contain <c>/</c>, which does not round-trip safely through a single route
    /// segment.</summary>
    public static IEndpointRouteBuilder MapGetRedirectByFromPathEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/lookup", HandleGetByFromPathAsync)
            .WithName("CmsGetRedirectByFromPath")
            .WithSummary("ค้นหากฎ redirect จาก path ต้นทาง")
            .Produces<RedirectResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps DELETE /api/cms/admin/redirects/{id}.</summary>
    public static IEndpointRouteBuilder MapDeleteRedirectEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/{id:guid}", HandleDeleteAsync)
            .WithName("CmsDeleteRedirect")
            .WithSummary("ลบกฎ redirect")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/cms/admin/redirects?page=&amp;pageSize=.</summary>
    public static IEndpointRouteBuilder MapListRedirectsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListAsync)
            .WithName("CmsListRedirects")
            .WithSummary("รายการกฎ redirect ทั้งหมด")
            .Produces<PagedResult<RedirectResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreateRedirectCommand command,
        RedirectService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/cms/admin/redirects/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleGetByFromPathAsync(
        string fromPath,
        RedirectService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByFromPathAsync(fromPath, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleDeleteAsync(
        Guid id,
        RedirectService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleListAsync(
        RedirectService service,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
