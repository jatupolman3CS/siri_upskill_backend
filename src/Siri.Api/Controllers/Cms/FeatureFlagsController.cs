using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Cms.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Cms;

[ApiController]
[Route("api/cms")]
[Tags("Cms")]
public class FeatureFlagsController : ControllerBase
{
    [HttpGet("feature-flags")]
    [AllowAnonymous]
    [EndpointName("GetPublicFeatureFlags")]
    [EndpointSummary("ดึงรายการ Feature Flags ที่เปิดใช้งาน")]
    [ProducesResponseType(typeof(IReadOnlyList<FeatureFlagResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetPublicFlags(
        [FromServices] FeatureFlagService service,
        CancellationToken cancellationToken)
    {
        var flags = await service.GetEnabledFlagsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(flags);
    }

    [HttpGet("admin/feature-flags")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("GetAdminFeatureFlags")]
    [EndpointSummary("ดึงรายการ Feature Flags ทั้งหมดสำหรับผู้ดูแลระบบ")]
    [ProducesResponseType(typeof(IReadOnlyList<FeatureFlagResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetAdminFlags(
        [FromServices] FeatureFlagService service,
        CancellationToken cancellationToken)
    {
        var flags = await service.GetAllFlagsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(flags);
    }

    [HttpPut("admin/feature-flags/{key}")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("UpsertFeatureFlag")]
    [EndpointSummary("สร้างหรืออัปเดตสถานะ Feature Flag")]
    [ProducesResponseType(typeof(FeatureFlagResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> UpsertFlag(
        [FromRoute] string key,
        [FromBody] UpsertFeatureFlagRequest request,
        [FromServices] FeatureFlagService service,
        CancellationToken cancellationToken)
    {
        var result = await service.UpsertFlagAsync(key, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
