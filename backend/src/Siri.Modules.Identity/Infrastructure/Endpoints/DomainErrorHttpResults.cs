using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Infrastructure.Endpoints;

/// <summary>
/// Maps a failed <see cref="Result"/>'s <see cref="DomainError"/> to an RFC 9457 <c>ProblemDetails</c>
/// HTTP response (backend.md: "Error → RFC 9457 ProblemDetails เสมอ; ใส่ traceId ทุกครั้ง"). Same
/// scoping note as <see cref="ValidationEndpointFilter{T}"/> — kept local to this module for now.
/// </summary>
public static class DomainErrorHttpResults
{
    public static IResult ToProblemHttpResult(this DomainError error, HttpContext httpContext)
    {
        var statusCode = error.Code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "validation" => StatusCodes.Status400BadRequest,
            "forbidden" => StatusCodes.Status403Forbidden,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        return Results.Problem(
            statusCode: statusCode,
            title: error.Message,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = traceId,
                ["errorCode"] = error.Code,
            });
    }
}
