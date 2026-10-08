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
    /// <summary>
    /// Suffix convention for "an external provider/integration this feature depends on is not
    /// configured in this deployment" (<c>video.provider_not_configured</c>,
    /// <c>payment.provider_not_configured</c>, <c>email.provider_not_configured</c>, ...). These are
    /// operator misconfigurations, not client mistakes, so they answer 503 instead of the generic 400.
    /// </summary>
    public const string NotConfiguredCodeSuffix = "_not_configured";

    /// <summary>
    /// Same meaning for a code whose last segment is literally <c>not_configured</c> (e.g.
    /// <c>google.not_configured</c> from <c>Siri.Integrations.Google</c>), which the <see cref="NotConfiguredCodeSuffix"/>
    /// check alone would miss (it needs an underscore right before the word).
    /// </summary>
    public const string DotNotConfiguredCodeSuffix = ".not_configured";

    /// <summary>
    /// Members the ProblemDetails body owns itself. A caller-supplied <see cref="DomainError.Extensions"/>
    /// key with one of these names is ignored, so a feature can never spoof <c>traceId</c>/<c>errorCode</c>/etc.
    /// </summary>
    private static readonly HashSet<string> ReservedExtensionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "traceId", "errorCode", "reason", "type", "title", "status", "detail", "instance",
    };

    public static IResult ToProblemHttpResult(this DomainError error, HttpContext httpContext)
    {
        var statusCode = error.Code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "validation" => StatusCodes.Status400BadRequest,
            "forbidden" => StatusCodes.Status403Forbidden,
            "conflict" => StatusCodes.Status409Conflict,
            "unavailable" => StatusCodes.Status503ServiceUnavailable,
            _ when error.Code.EndsWith(NotConfiguredCodeSuffix, StringComparison.Ordinal)
                || error.Code.EndsWith(DotNotConfiguredCodeSuffix, StringComparison.Ordinal) => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = traceId,
            ["errorCode"] = error.Code,
        };

        if (!string.IsNullOrEmpty(error.Reason))
        {
            extensions["reason"] = error.Reason;
        }

        if (error.Extensions is { Count: > 0 } additional)
        {
            foreach (var (key, value) in additional)
            {
                if (!ReservedExtensionKeys.Contains(key))
                {
                    extensions[key] = value;
                }
            }
        }

        return Results.Problem(
            statusCode: statusCode,
            title: error.Message,
            extensions: extensions);
    }
}
