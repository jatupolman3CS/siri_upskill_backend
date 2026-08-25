using System.ComponentModel.DataAnnotations;

namespace Siri.Api.Observability;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Observability"). Options pattern +
/// <c>ValidateOnStart()</c> per backend.md's Configuration section, same shape as
/// <c>JwtOptions</c>/<c>RedisOptions</c>.
/// <para>
/// P0-13 (ARCHITECTURE.md §5 "Observability: OpenTelemetry (trace/metric/log) → OTLP"). One base
/// OTLP endpoint drives all three signals: traces + metrics go through the OpenTelemetry SDK's
/// <c>UseOtlpExporter(protocol, uri)</c>, logs through Serilog's OpenTelemetry sink — both accept
/// the same base URL (no <c>/v1/logs</c>-style path; the SDK and the sink each append the standard
/// signal paths themselves when the protocol is HTTP, and use the URL as the channel address for
/// gRPC). A null/blank <see cref="OtlpEndpoint"/> means "no collector in this environment": the
/// instrumentation still runs (spans/metrics are cheap no-ops with no exporter attached, and logs
/// keep going to console JSON), so flipping export on later is a pure config change — matching
/// DEPLOYMENT.md where Seq/the collector only exists on the VPS, not on dev machines or CI.
/// </para>
/// </summary>
public sealed class ObservabilityOptions : IValidatableObject
{
    public const string SectionName = "Observability";

    /// <summary>Base OTLP collector endpoint, e.g. <c>http://localhost:4317</c> (gRPC) or
    /// <c>http://localhost:4318</c> (HTTP/protobuf). Null or blank disables all OTLP export —
    /// deliberately not <c>[Required]</c>, see class doc comment.</summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>Transport for all three signals. Config binder parses the string value
    /// case-insensitively ("Grpc"/"HttpProtobuf").</summary>
    public OtlpTransport OtlpProtocol { get; set; } = OtlpTransport.Grpc;

    /// <summary>Becomes the OTLP resource's <c>service.name</c> on every signal — what Seq/any
    /// OTLP backend groups by.</summary>
    [Required]
    public string ServiceName { get; set; } = "Siri.Api";

    /// <summary>True when an OTLP endpoint is configured for this environment.</summary>
    public bool OtlpExportEnabled => !string.IsNullOrWhiteSpace(OtlpEndpoint);

    /// <summary>The validated endpoint as a <see cref="Uri"/>. Only call when
    /// <see cref="OtlpExportEnabled"/> is true.</summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="OtlpEndpoint"/> isn't parseable as an absolute URI at all (e.g. no scheme). Program.cs
    /// calls this to configure Serilog/the OpenTelemetry SDK before <c>ValidateOnStart()</c> (registered
    /// separately) gets a chance to run, so this throws its own clear message instead of leaking a raw
    /// <see cref="UriFormatException"/> out of the <c>Uri</c> constructor for that one shape of bad
    /// input — everything else (wrong scheme, e.g. <c>ftp://</c>) parses fine here and is still caught
    /// by <see cref="Validate"/> below.
    /// </exception>
    public Uri GetOtlpEndpointUri()
    {
        if (!Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"'{SectionName}:{nameof(OtlpEndpoint)}' is not a valid absolute URL (got '{OtlpEndpoint}').");
        }

        return uri;
    }

    /// <summary>Called by <c>ValidateDataAnnotations()</c> (Options-pattern validation runs
    /// <c>IValidatableObject</c> too). A custom check instead of <c>[Url]</c> because blank must
    /// stay valid-and-disabled while a present-but-garbage value must fail at startup, not at the
    /// first export attempt.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!OtlpExportEnabled)
        {
            yield break;
        }

        if (!Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            yield return new ValidationResult(
                $"'{SectionName}:{nameof(OtlpEndpoint)}' must be an absolute http(s) URL (got '{OtlpEndpoint}').",
                [nameof(OtlpEndpoint)]);
        }
    }
}

/// <summary>Mirrors the two OTLP transports both exporters support. Our own enum (rather than
/// exposing <c>OpenTelemetry.Exporter.OtlpExportProtocol</c> in configuration) because the Serilog
/// sink has its own separate <c>OtlpProtocol</c> enum — Program.cs maps this one value onto both.</summary>
public enum OtlpTransport
{
    Grpc,
    HttpProtobuf,
}
