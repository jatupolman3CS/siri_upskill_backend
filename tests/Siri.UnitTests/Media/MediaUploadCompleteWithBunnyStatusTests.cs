using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Video.Bunny;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Media;

/// <summary>
/// Wires the real <see cref="BunnyVideoProvider"/> (HTTP faked at the handler) into the real
/// <see cref="MediaUploadSessionService"/> so the "upload finished" step is checked against what Bunny
/// actually reports, not against a hand-set <c>VideoProcessingStatus</c>. Regression for Bunny status
/// <c>1 = Uploaded</c> being read as "still uploading", which made a fully delivered upload answer 409.
/// </summary>
public sealed class MediaUploadCompleteWithBunnyStatusTests
{
    [Fact]
    public async Task CompleteAsync_BunnyReportsUploaded_CompletesSessionAndMarksAssetProcessing()
    {
        var (service, asset, session) = Arrange(bunnyStatus: 1);

        var result = await service.CompleteAsync(asset.UPLOADED_BY_USER_ID, session.MEDIA_UPLOAD_SESSION_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaUploadSessionStatus.Completed, session.STATUS);
        Assert.Equal(MediaAssetStatus.Processing, asset.STATUS);
    }

    [Fact]
    public async Task CompleteAsync_BunnyReportsCreated_StillAnswersConflictBecauseNoBytesArrivedYet()
    {
        var (service, asset, session) = Arrange(bunnyStatus: 0);

        var result = await service.CompleteAsync(asset.UPLOADED_BY_USER_ID, session.MEDIA_UPLOAD_SESSION_ID, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(MediaUploadSessionStatus.Pending, session.STATUS);
        Assert.Equal(MediaAssetStatus.Uploading, asset.STATUS);
    }

    private static (MediaUploadSessionService Service, MEDIA_ASSET Asset, MEDIA_UPLOAD_SESSION Session) Arrange(int bunnyStatus)
    {
        var options = new VideoProviderOptions
        {
            LibraryId = "12345",
            ApiKey = "test-api-key-abc123",
            ReadOnlyApiKey = "test-readonly-key",
            PullZone = "my-pull-zone",
            CdnHostname = "my-pull-zone.b-cdn.net",
            TokenAuthenticationKey = "test-token-auth-key-secret",
        };
        var body = $$"""{"guid":"vid-1","title":"t","status":{{bunnyStatus}},"length":0.0}""";
        var provider = new BunnyVideoProvider(
            new StubHttpClientFactory(new StubHandler(body)),
            Options.Create(options),
            NullLogger<BunnyVideoProvider>.Instance);

        var assets = new InMemoryAssetRepository();
        var sessions = new InMemorySessionRepository();
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", Guid.NewGuid(), true);
        assets.Add(asset);
        var session = MEDIA_UPLOAD_SESSION.Create(asset.MEDIA_ASSET_ID, "https://video.bunnycdn.com/tusupload", DateTime.UtcNow.AddMinutes(30));
        sessions.Add(session);

        var service = new MediaUploadSessionService(sessions, assets, provider, new FixedClock());
        return (service, asset, session);
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class InMemoryAssetRepository : IMediaAssetRepository
    {
        private readonly Dictionary<Guid, MEDIA_ASSET> _assets = [];

        public Task<MEDIA_ASSET?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_assets.TryGetValue(id, out var asset) ? asset : null);

        public Task<MEDIA_ASSET?> GetByProviderAssetIdAsync(string providerAssetId, CancellationToken cancellationToken) =>
            Task.FromResult(_assets.Values.FirstOrDefault(a => a.PROVIDER_ASSET_ID == providerAssetId));

        public Task<(IReadOnlyList<MEDIA_ASSET> Items, int TotalCount)> GetPagedByUploaderAsync(
            Guid uploadedByUserId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var items = _assets.Values.Where(a => a.UPLOADED_BY_USER_ID == uploadedByUserId).ToList();
            return Task.FromResult(((IReadOnlyList<MEDIA_ASSET>)items, items.Count));
        }

        public void Add(MEDIA_ASSET mediaAsset) => _assets[mediaAsset.MEDIA_ASSET_ID] = mediaAsset;

        public void Remove(MEDIA_ASSET mediaAsset) => _assets.Remove(mediaAsset.MEDIA_ASSET_ID);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemorySessionRepository : IMediaUploadSessionRepository
    {
        private readonly Dictionary<Guid, MEDIA_UPLOAD_SESSION> _sessions = [];

        public Task<MEDIA_UPLOAD_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_sessions.TryGetValue(id, out var session) ? session : null);

        public void Add(MEDIA_UPLOAD_SESSION uploadSession) => _sessions[uploadSession.MEDIA_UPLOAD_SESSION_ID] = uploadSession;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
