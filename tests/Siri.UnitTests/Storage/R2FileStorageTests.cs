using System.Net;
using Amazon.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Storage;
using Xunit;

namespace Siri.UnitTests.Storage;

public sealed class R2FileStorageTests
{
    private const string AccountId = "0123456789abcdef0123456789abcdef";
    private const string Bucket = "siri-materials";
    private const string Key = "teaching-materials/courses/c/episodes/e/a.pdf";

    private static R2StorageOptions ConfiguredOptions() => new()
    {
        AccountId = AccountId,
        AccessKeyId = "test-access-key-id",
        SecretAccessKey = "test-secret-access-key",
        BucketName = Bucket,
    };

    private static R2FileStorage Create(R2StorageOptions options, RecordingHandler? handler = null) =>
        new(Options.Create(options), NullLogger<R2FileStorage>.Instance, handler is null ? null : new StubHttpClientFactory(handler));

    [Fact]
    public async Task UploadAsync_WhenNotConfigured_FailsWithoutAnyHttpCall()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var sut = Create(new R2StorageOptions(), handler);

        var result = await sut.UploadAsync(Key, new MemoryStream([1, 2, 3]), 3, "application/pdf", "a.pdf", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrors.ProviderNotConfiguredCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UploadAsync_PutsToThePathStyleR2EndpointWithAttachmentDisposition()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var sut = Create(ConfiguredOptions(), handler);
        byte[] payload = [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[500]];

        var result = await sut.UploadAsync(Key, new MemoryStream(payload), payload.Length, "application/pdf", "บทที่ 1 slides.pdf", CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(Key, result.Value.Key);
        Assert.Equal(payload.Length, result.Value.SizeBytes);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal($"{AccountId}.r2.cloudflarestorage.com", request.Uri.Host);
        Assert.Equal("https", request.Uri.Scheme);
        Assert.Equal($"/{Bucket}/{Key}", request.Uri.AbsolutePath);
        Assert.Equal("application/pdf", request.Header("Content-Type"));

        var disposition = request.Header("Content-Disposition");
        Assert.NotNull(disposition);
        Assert.StartsWith("attachment;", disposition);
        Assert.Contains("filename*=UTF-8''", disposition);
        Assert.Contains(Uri.EscapeDataString("บทที่ 1 slides.pdf"), disposition);

        Assert.Equal(payload, request.Body);
    }

    [Fact]
    public async Task UploadAsync_UsesRegionAutoSigV4AndNoAwsOnlyChecksumFeatures()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var sut = Create(ConfiguredOptions(), handler);

        await sut.UploadAsync(Key, new MemoryStream([1, 2, 3]), 3, "application/pdf", "a.pdf", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        var authorization = request.Header("Authorization");
        Assert.NotNull(authorization);
        Assert.StartsWith("AWS4-HMAC-SHA256", authorization);
        Assert.Contains("/auto/s3/aws4_request", authorization);

        // R2 does not implement the SDK's default integrity-checksum trailers / streaming SigV4.
        Assert.Null(request.Header("x-amz-sdk-checksum-algorithm"));
        Assert.Null(request.Header("x-amz-trailer"));
        Assert.DoesNotContain("aws-chunked", request.Header("Content-Encoding") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("STREAMING", request.Header("x-amz-content-sha256") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAsync_WhenProviderRejectsTheRequest_ReturnsGenericUnavailableWithoutProviderText()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.Forbidden,
            """<?xml version="1.0"?><Error><Code>AccessDenied</Code><Message>secret provider detail</Message></Error>""");
        using var sut = Create(ConfiguredOptions(), handler);

        var result = await sut.UploadAsync(Key, new MemoryStream([1, 2, 3]), 3, "application/pdf", "a.pdf", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unavailable", result.Error.Code);
        Assert.DoesNotContain("secret provider detail", result.Error.Message);
        Assert.DoesNotContain("AccessDenied", result.Error.Message);
    }

    [Fact]
    public async Task DeleteAsync_IssuesADeleteOnTheObject()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent);
        using var sut = Create(ConfiguredOptions(), handler);

        var result = await sut.DeleteAsync(Key, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"/{Bucket}/{Key}", request.Uri.AbsolutePath);
    }

    [Fact]
    public async Task DeleteAsync_WhenNotConfigured_Fails()
    {
        using var sut = Create(new R2StorageOptions());

        var result = await sut.DeleteAsync(Key, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrors.ProviderNotConfiguredCode, result.Error.Code);
    }

    [Fact]
    public async Task GetSignedUrlAsync_BuildsAShortLivedSignedGetUrlWithoutANetworkCall()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var sut = Create(ConfiguredOptions(), handler);

        var result = await sut.GetSignedUrlAsync(Key, TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var uri = new Uri(result.Value);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal($"{AccountId}.r2.cloudflarestorage.com", uri.Host);
        Assert.Equal($"/{Bucket}/{Key}", uri.AbsolutePath);
        Assert.Contains("X-Amz-Algorithm=AWS4-HMAC-SHA256", uri.Query);
        Assert.Contains("X-Amz-Expires=300", uri.Query);
        Assert.Contains("X-Amz-Signature=", uri.Query);
        Assert.Contains("/auto/s3/aws4_request", Uri.UnescapeDataString(uri.Query));
        Assert.DoesNotContain("test-secret-access-key", result.Value);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetSignedUrlAsync_HonoursTheEndpointOverride()
    {
        var options = ConfiguredOptions();
        options.Endpoint = $"https://{AccountId}.eu.r2.cloudflarestorage.com/";
        using var sut = Create(options);

        var result = await sut.GetSignedUrlAsync(Key, TimeSpan.FromMinutes(2), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"{AccountId}.eu.r2.cloudflarestorage.com", new Uri(result.Value).Host);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    [InlineData(7 * 24 * 3600 + 1)]
    public async Task GetSignedUrlAsync_RejectsAnUnreasonableLifetime(int seconds)
    {
        using var sut = Create(ConfiguredOptions());

        var result = await sut.GetSignedUrlAsync(Key, TimeSpan.FromSeconds(seconds), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetSignedUrlAsync_WhenNotConfigured_Fails()
    {
        using var sut = Create(new R2StorageOptions());

        var result = await sut.GetSignedUrlAsync(Key, TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrors.ProviderNotConfiguredCode, result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/leading-slash.pdf")]
    [InlineData("a/../b.pdf")]
    [InlineData("a\\b.pdf")]
    [InlineData("a\nb.pdf")]
    public async Task AllOperations_RejectAnInvalidKey(string key)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var sut = Create(ConfiguredOptions(), handler);

        var upload = await sut.UploadAsync(key, new MemoryStream([1]), 1, "application/pdf", "a.pdf", CancellationToken.None);
        var sign = await sut.GetSignedUrlAsync(key, TimeSpan.FromMinutes(1), CancellationToken.None);
        var delete = await sut.DeleteAsync(key, CancellationToken.None);

        Assert.True(upload.IsFailure);
        Assert.True(sign.IsFailure);
        Assert.True(delete.IsFailure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void IsValidKey_RejectsAKeyLongerThanTheDatabaseColumn()
    {
        Assert.True(R2FileStorage.IsValidKey(new string('a', R2FileStorage.MaxKeyLength)));
        Assert.False(R2FileStorage.IsValidKey(new string('a', R2FileStorage.MaxKeyLength + 1)));
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, byte[] Body)
    {
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    private sealed class RecordingHandler(HttpStatusCode status, string? responseBody = null) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }

            byte[] body = [];
            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] = string.Join(",", header.Value);
                }

                body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            }

            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, headers, body));

            var response = new HttpResponseMessage(status)
            {
                Content = responseBody is null ? new ByteArrayContent([]) : new StringContent(responseBody),
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"d41d8cd98f00b204e9800998ecf8427e\"");

            return response;
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    }
}
