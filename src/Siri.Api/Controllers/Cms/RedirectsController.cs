using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Cms.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Cms;

[ApiController]
[Route("api/cms/admin/redirects")]
[Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
[Tags("Cms Admin")]
public class RedirectsController : ControllerBase
{
    [HttpPost("")]
    [EndpointName("CmsCreateRedirect")]
    [EndpointSummary("สร้างกฎ redirect ใหม่")]
    [ProducesResponseType(typeof(RedirectResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Create(
        [FromBody] CreateRedirectCommand command,
        [FromServices] RedirectService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/cms/admin/redirects/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("lookup")]
    [EndpointName("CmsGetRedirectByFromPath")]
    [EndpointSummary("ค้นหากฎ redirect จาก path ต้นทาง")]
    [ProducesResponseType(typeof(RedirectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetByFromPath(
        [FromQuery] string fromPath,
        [FromServices] RedirectService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByFromPathAsync(fromPath, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("{id:guid}")]
    [EndpointName("CmsDeleteRedirect")]
    [EndpointSummary("ลบกฎ redirect")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Delete(
        [FromRoute] Guid id,
        [FromServices] RedirectService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("")]
    [EndpointName("CmsListRedirects")]
    [EndpointSummary("รายการกฎ redirect ทั้งหมด")]
    [ProducesResponseType(typeof(PagedResult<RedirectResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] RedirectService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
