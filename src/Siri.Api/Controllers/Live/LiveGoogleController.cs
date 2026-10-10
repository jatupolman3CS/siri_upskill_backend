using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Siri.Api.Configuration;
using Siri.Modules.Live.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Live;

/// <summary>
/// The instructor's Google connection (P11-03): status, connect, the OAuth callback, disconnect. Every action states its
/// authorization explicitly (there is no fallback policy). Instructors only; an administrator is not special-cased because the
/// connection belongs to the person whose calendar it is. The caller's identity always comes from <see cref="IUserContext"/> —
/// nothing here accepts a user id from the request.
/// <para>
/// Responses carry <c>Cache-Control: no-store</c>: they describe one person's account.
/// </para>
/// </summary>
[ApiController]
[Route("api/live/instructor/google")]
[Tags("Live")]
public class LiveGoogleController : ControllerBase
{
    [HttpGet("status")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
    [EndpointName("LiveGetGoogleConnectionStatus")]
    [EndpointSummary("ดูสถานะการเชื่อมต่อ Google Calendar ของผู้สอน")]
    [ProducesResponseType(typeof(GoogleConnectionStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> GetStatus(
        [FromServices] InstructorGoogleAccountService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.GetStatusAsync(userId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>POST (not a redirecting GET): the SPA's bearer token lives in memory and cannot ride on a top-level navigation, and the call creates
    /// server-side state. The frontend sends the browser to the returned <c>authorizationUrl</c>.</summary>
    [HttpPost("connect")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
    [EndpointName("LiveConnectGoogle")]
    [EndpointSummary("เริ่มเชื่อมต่อ Google Calendar — คืน URL ของหน้ายินยอม Google")]
    [ProducesResponseType(typeof(GoogleConnectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> Connect(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GoogleConnectCommand? command,
        [FromServices] InstructorGoogleAccountService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.BeginConnectAsync(userId, command?.ReturnPath, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    /// <summary>
    /// The second, optional consent (P11-13): the recording scopes for the automatic recording import, on top of the calendar ones. Same shape and the same
    /// rate limit as <see cref="Connect"/>; the same callback finishes it. <b>409 <c>live.recording_not_available</c></b> when the feature is switched off
    /// (<c>Live:Recording:AutoImport:Enabled</c>) or the connected account is not a Google Workspace account.
    /// </summary>
    [HttpPost("recording-access/connect")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
    [EndpointName("LiveConnectGoogleRecordingAccess")]
    [EndpointSummary("เริ่มขออนุญาตเข้าถึงบันทึก Google Meet (Workspace เท่านั้น) — คืน URL ของหน้ายินยอม Google")]
    [ProducesResponseType(typeof(GoogleConnectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> ConnectRecordingAccess(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GoogleConnectCommand? command,
        [FromServices] InstructorGoogleAccountService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.BeginRecordingAccessConnectAsync(userId, command?.ReturnPath, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    /// <summary>
    /// Where Google sends the browser back. <b>Anonymous by necessity</b> (it is a navigation without the SPA's bearer token); trust comes from the
    /// single-use <c>state</c> created by <see cref="Connect"/>. It <b>always answers 302</b> — success and every failure alike — to the frontend
    /// with <c>?google=connected</c> or <c>?google=error&amp;reason=...</c>, and never puts the code, a token or an e-mail address in the URL.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterConfiguration.LiveGoogleCallbackPolicyName)]
    [EndpointName("LiveGoogleOAuthCallback")]
    [EndpointSummary("Callback ของ Google OAuth — ตรวจ state แล้ว redirect กลับหน้า FE เสมอ")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromServices] InstructorGoogleAccountService service,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var outcome = await service.CompleteConnectAsync(code, state, error, cancellationToken).ConfigureAwait(false);

        return Results.Redirect(outcome.RedirectUrl);
    }

    [HttpDelete("")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
    [EndpointName("LiveDisconnectGoogle")]
    [EndpointSummary("ยกเลิกการเชื่อมต่อ Google Calendar (ทำซ้ำได้)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> Disconnect(
        [FromServices] InstructorGoogleAccountService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        await service.DisconnectAsync(userId, cancellationToken).ConfigureAwait(false);

        return Results.NoContent();
    }
}
