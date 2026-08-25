using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Siri.SharedKernel;

/// <summary>
/// Maps a failed <see cref="Result"/>'s <see cref="DomainError"/> to an RFC 9457 <c>ProblemDetails</c>
/// HTTP response (backend.md: "Error → RFC 9457 ProblemDetails เสมอ; ใส่ traceId ทุกครั้ง").
/// <para>
/// Originally lived in <c>Siri.Modules.Identity</c>, same scoping note as
/// <see cref="ValidationEndpointFilter{T}"/> — relocated here for P1-01 once Catalog became a second
/// consumer.
/// </para>
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
