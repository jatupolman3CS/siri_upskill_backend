using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Siri.Api.Observability;

namespace Siri.UnitTests.Observability;

public class ObservabilityOptionsTests
{
    /// <summary>Runs the exact validation Options-pattern <c>ValidateDataAnnotations()</c> runs at
    /// startup: attributes + <see cref="IValidatableObject.Validate"/>, all properties.</summary>
    private static List<ValidationResult> Validate(ObservabilityOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Defaults_ExportDisabled_AndValid()
    {
        var options = new ObservabilityOptions();

        Assert.False(options.OtlpExportEnabled);
        Assert.Equal(OtlpTransport.Grpc, options.OtlpProtocol);
        Assert.Equal("siriupskill-api", options.ServiceName);
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OtlpEndpoint_NullOrBlank_MeansDisabledAndStillValid(string? endpoint)
    {
        var options = new ObservabilityOptions { OtlpEndpoint = endpoint };

        Assert.False(options.OtlpExportEnabled);
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData("http://localhost:4317")]
    [InlineData("https://collector.internal:4318")]
    public void OtlpEndpoint_AbsoluteHttpUrl_EnablesExportAndValidates(string endpoint)
    {
        var options = new ObservabilityOptions { OtlpEndpoint = endpoint };

        Assert.True(options.OtlpExportEnabled);
        Assert.Empty(Validate(options));
        Assert.Equal(new Uri(endpoint), options.GetOtlpEndpointUri());
    }

    [Theory]
    [InlineData("localhost:4317")] // no scheme — Uri parses this as scheme "localhost", not a host
    [InlineData("not a url")]
    [InlineData("ftp://collector:21")] // absolute but not http(s) — neither exporter speaks it
    public void OtlpEndpoint_PresentButNotHttpUrl_FailsValidation(string endpoint)
    {
        var options = new ObservabilityOptions { OtlpEndpoint = endpoint };

        var results = Validate(options);

        var failure = Assert.Single(results);
        Assert.Contains(nameof(ObservabilityOptions.OtlpEndpoint), failure.MemberNames);
    }

    [Fact]
    public void GetOtlpEndpointUri_UnparseableEndpoint_ThrowsInvalidOperationExceptionNotUriFormatException()
    {
        // Program.cs calls GetOtlpEndpointUri() to wire up Serilog/the OTel SDK before
        // ValidateOnStart() (registered separately) runs — for an endpoint shape Uri can't parse at
        // all (unlike a wrong-but-parseable scheme, e.g. "ftp://..."), this must fail with a clear,
        // typed exception rather than a raw UriFormatException leaking out of the Uri constructor.
        var options = new ObservabilityOptions { OtlpEndpoint = "not a url" };

        var ex = Assert.Throws<InvalidOperationException>(() => options.GetOtlpEndpointUri());
        Assert.Contains(nameof(ObservabilityOptions.OtlpEndpoint), ex.Message);
    }

    [Fact]
    public void ServiceName_Blank_FailsValidation()
    {
        var options = new ObservabilityOptions { ServiceName = "" };

        var results = Validate(options);

        var failure = Assert.Single(results);
        Assert.Contains(nameof(ObservabilityOptions.ServiceName), failure.MemberNames);
    }

    [Fact]
    public void Binding_FromObservabilitySection_PopulatesEveryField()
    {
        // Same GetSection(...).Get<T>() path Program.cs uses (it reads the section directly, before
        // DI exists — see its P0-13 comment), including case-insensitive enum parsing by the binder.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:OtlpEndpoint"] = "http://seq:5341/ingest/otlp",
                ["Observability:OtlpProtocol"] = "httpProtobuf",
                ["Observability:ServiceName"] = "Siri.Api.Staging",
            })
            .Build();

        var options = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>();

        Assert.NotNull(options);
        Assert.Equal("http://seq:5341/ingest/otlp", options!.OtlpEndpoint);
        Assert.Equal(OtlpTransport.HttpProtobuf, options.OtlpProtocol);
        Assert.Equal("Siri.Api.Staging", options.ServiceName);
        Assert.True(options.OtlpExportEnabled);
    }

    [Fact]
    public void Binding_SectionMissing_LeavesDefaultsInPlace()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();

        Assert.False(options.OtlpExportEnabled);
        Assert.Equal(OtlpTransport.Grpc, options.OtlpProtocol);
        Assert.Equal("siriupskill-api", options.ServiceName);
    }

    [Fact]
    public void OtlpApiKey_Blank_IsValidAndMeansNoAuthHeader()
    {
        var options = new ObservabilityOptions { OtlpEndpoint = "http://localhost:4317" };

        Assert.Null(options.OtlpApiKey);
        Assert.Empty(Validate(options));
    }

    [Fact]
    public void SectionName_MatchesConvention()
    {
        Assert.Equal("Observability", ObservabilityOptions.SectionName);
    }
}
