using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Siri.Api.ErrorHandling;

/// <summary>
/// Last-resort handler for exceptions that escape endpoint code — i.e. real system failures
/// (DB down, bad config), never expected/business errors (those are modelled with
/// <c>Siri.SharedKernel.Result</c> and never thrown; see backend.md). Maps to an RFC 9457
/// <c>ProblemDetails</c> response and always includes a <c>traceId</c> so a report from a user can
/// be tied back to server-side logs.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier;

        logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", traceId);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "The request could not be completed. Contact support with the trace id if this persists.",
        };
        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = problemDetails.Status.Value;

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
