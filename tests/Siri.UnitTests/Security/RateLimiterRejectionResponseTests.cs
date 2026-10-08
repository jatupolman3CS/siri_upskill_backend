using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Siri.Api.Configuration;

namespace Siri.UnitTests.Security;

/// <summary>
/// D5 (integrator-qa): a rate-limited request used to get a bare 429 — empty body, no Retry-After, cacheable. Every 429 is now an RFC 9457 ProblemDetails
/// (<c>errorCode: rate_limited</c>, <c>traceId</c>) + <c>Retry-After</c> in whole seconds + <c>Cache-Control: no-store</c>, for every policy (the callback is global).
/// </summary>
public sealed class RateLimiterRejectionResponseTests
{
    private sealed class FakeLease(TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } value && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }
    }

    private static async Task<(DefaultHttpContext Http, JsonElement Body)> RejectAsync(TimeSpan? retryAfter)
    {
        var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "trace-429" };
        http.Response.Body = new MemoryStream();

        await RateLimiterConfiguration.WriteRejectedResponseAsync(
            new OnRejectedContext { HttpContext = http, Lease = new FakeLease(retryAfter) },
            CancellationToken.None);

        http.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(http.Response.Body);
        return (http, document.RootElement.Clone());
    }

    [Fact]
    public async Task Rejected_IsA429ProblemDetails_WithErrorCodeAndTraceId()
    {
        var (http, body) = await RejectAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(StatusCodes.Status429TooManyRequests, http.Response.StatusCode);
        Assert.StartsWith("application/problem+json", http.Response.ContentType);
        Assert.Equal(429, body.GetProperty("status").GetInt32());
        Assert.Equal("Too many requests", body.GetProperty("title").GetString());
        Assert.Equal("rate_limited", body.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Rejected_HasNoReasonMember_BecauseClientsRecogniseARateLimitByStatusAlone()
    {
        var (_, body) = await RejectAsync(TimeSpan.FromSeconds(30));

        Assert.False(body.TryGetProperty("reason", out _));
    }

    [Fact]
    public async Task Rejected_IsNeverCacheable()
    {
        var (http, _) = await RejectAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData(30.0, "30")]
    [InlineData(17.2, "18")] // rounded UP: coming back earlier would be refused again
    [InlineData(0.2, "1")] // never "0"
    [InlineData(59.999, "60")]
    public async Task Rejected_RetryAfter_IsWholeSecondsFromTheLeaseMetadata(double seconds, string expectedHeader)
    {
        var (http, _) = await RejectAsync(TimeSpan.FromSeconds(seconds));

        Assert.Equal(expectedHeader, http.Response.Headers.RetryAfter.ToString());
    }

    [Theory]
    [InlineData(null)] // a limiter that does not know when it reopens: the header is omitted, never invented
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public async Task Rejected_RetryAfter_IsOmittedWhenTheLimiterDoesNotSayWhen(double? seconds)
    {
        var (http, body) = await RejectAsync(seconds is null ? null : TimeSpan.FromSeconds(seconds.Value));

        Assert.False(http.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal("rate_limited", body.GetProperty("errorCode").GetString()); // still a full problem body
    }

    [Fact]
    public void Configure_InstallsTheSharedCallback_ForTheWholeApi()
    {
        var options = new RateLimiterOptions();

        RateLimiterConfiguration.Configure(options, isDevelopment: false);

        Assert.NotNull(options.OnRejected);
        Assert.Equal(StatusCodes.Status429TooManyRequests, options.RejectionStatusCode);
    }
}
