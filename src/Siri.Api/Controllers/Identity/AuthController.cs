using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Identity.Features.AnonymizeAccount;
using Siri.Modules.Identity.Features.ConfirmEmail;
using Siri.Modules.Identity.Features.DataExport;
using Siri.Modules.Identity.Features.ForgotPassword;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Features.Logout;
using Siri.Modules.Identity.Features.Refresh;
using Siri.Modules.Identity.Features.Register;
using Siri.Modules.Identity.Features.ResetPassword;
using Siri.Modules.Identity.Infrastructure.Endpoints;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Identity;

[ApiController]
[Route("api/identity")]
[Tags("Identity")]
public class AuthController : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [EndpointName("IdentityRegister")]
    [EndpointSummary("สมัครสมาชิกใหม่และส่งอีเมลยืนยันบัญชี")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> Register(
        [FromBody] RegisterCommand command,
        [FromServices] RegisterHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [EndpointName("IdentityConfirmEmail")]
    [EndpointSummary("ยืนยันอีเมลด้วยโทเคนจากลิงก์ยืนยัน")]
    [ProducesResponseType(typeof(ConfirmEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> ConfirmEmail(
        [FromBody] ConfirmEmailCommand command,
        [FromServices] ConfirmEmailHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [EndpointName("IdentityLogin")]
    [EndpointSummary("เข้าสู่ระบบด้วยอีเมลและรหัสผ่าน")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> Login(
        [FromBody] LoginCommand command,
        [FromServices] LoginHandler handler,
        CancellationToken cancellationToken)
    {
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await handler.HandleAsync(
            command,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            ipAddress,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblemHttpResult(HttpContext);
        }

        RefreshTokenCookie.Set(HttpContext, result.Value.RawRefreshToken, result.Value.RefreshTokenExpiresAtUtc);

        return Results.Ok(new LoginResponse(result.Value.AccessToken, result.Value.AccessTokenExpiresAtUtc));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [EndpointName("IdentityRefresh")]
    [EndpointSummary("ขอ access token ใหม่ด้วย refresh token cookie พร้อม rotation")]
    [ProducesResponseType(typeof(RefreshResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> Refresh(
        [FromServices] RefreshHandler handler,
        CancellationToken cancellationToken)
    {
        var rawRefreshToken = RefreshTokenCookie.Read(HttpContext) ?? string.Empty;
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var command = new RefreshCommand(
            rawRefreshToken,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            ipAddress);

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblemHttpResult(HttpContext);
        }

        RefreshTokenCookie.Set(HttpContext, result.Value.RawRefreshToken, result.Value.RefreshTokenExpiresAtUtc);

        return Results.Ok(new RefreshResponse(result.Value.AccessToken, result.Value.AccessTokenExpiresAtUtc));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [EndpointName("IdentityLogout")]
    [EndpointSummary("ออกจากระบบและยกเลิกโทเคนเซสชัน")]
    [ProducesResponseType(typeof(LogoutResponse), StatusCodes.Status200OK)]
    public async Task<IResult> Logout(
        [FromServices] LogoutHandler handler,
        CancellationToken cancellationToken)
    {
        var rawRefreshToken = RefreshTokenCookie.Read(HttpContext);
        await handler.HandleAsync(rawRefreshToken, cancellationToken).ConfigureAwait(false);
        RefreshTokenCookie.Clear(HttpContext);

        return Results.Ok(new LogoutResponse("ออกจากระบบเรียบร้อยแล้ว"));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [EndpointName("IdentityForgotPassword")]
    [EndpointSummary("ขอลิงก์ตั้งรหัสผ่านใหม่ทางอีเมล")]
    [ProducesResponseType(typeof(ForgotPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> ForgotPassword(
        [FromBody] ForgotPasswordCommand command,
        [FromServices] ForgotPasswordHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result.Value);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [EndpointName("IdentityResetPassword")]
    [EndpointSummary("ตั้งรหัสผ่านใหม่ด้วยโทเคนจากลิงก์ตั้งรหัสผ่านใหม่")]
    [ProducesResponseType(typeof(ResetPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> ResetPassword(
        [FromBody] ResetPasswordCommand command,
        [FromServices] ResetPasswordHandler handler,
        CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await handler.HandleAsync(command, ipAddress, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("data-export")]
    [Authorize]
    [EndpointName("DataExport")]
    [EndpointSummary("Exports personal data for the authenticated user under PDPA")]
    [ProducesResponseType(typeof(DataExportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> DataExport(
        [FromServices] IUserContext userContext,
        [FromServices] DataExportHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is null)
        {
            return Results.Unauthorized();
        }

        var command = new DataExportCommand(userContext.UserId.Value);
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("anonymize")]
    [Authorize]
    [EndpointName("AnonymizeAccount")]
    [EndpointSummary("Anonymizes user account per PDPA Right to Erasure")]
    [ProducesResponseType(typeof(AnonymizeAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Anonymize(
        [FromBody] AnonymizeAccountRequest request,
        [FromServices] IUserContext userContext,
        [FromServices] AnonymizeAccountHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is null)
        {
            return Results.Unauthorized();
        }

        var command = new AnonymizeAccountCommand(userContext.UserId.Value, request.Password, request.Confirmation);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await handler.HandleAsync(command, ipAddress, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
