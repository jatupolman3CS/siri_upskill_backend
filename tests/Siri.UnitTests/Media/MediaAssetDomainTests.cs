using Siri.Modules.Media.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Media;

public sealed class MediaAssetDomainTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    [Fact]
    public void Create_WithValidArgs_InitializesUploadingAsset()
    {
        var uploaderId = Guid.NewGuid();
        var asset = MEDIA_ASSET.Create("BunnyStream", "bunny-vid-123", uploaderId, true);

        Assert.NotEqual(Guid.Empty, asset.MEDIA_ASSET_ID);
        Assert.Equal("BunnyStream", asset.PROVIDER);
        Assert.Equal("bunny-vid-123", asset.PROVIDER_ASSET_ID);
        Assert.Equal(uploaderId, asset.UPLOADED_BY_USER_ID);
        Assert.True(asset.DRM_ENABLED);
        Assert.Equal(MediaAssetStatus.Uploading, asset.STATUS);
        Assert.Null(asset.READY_AT_UTC);
        Assert.Null(asset.ERROR_MESSAGE);
    }

    [Fact]
    public void Create_WithEmptyProvider_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            MEDIA_ASSET.Create("", "bunny-vid-123", Guid.NewGuid(), true));
    }

    [Fact]
    public void MarkProcessing_FromUploading_TransitionsToProcessing()
    {
        var asset = MEDIA_ASSET.Create("BunnyStream", "bunny-vid-123", Guid.NewGuid(), true);
        asset.MarkProcessing();

        Assert.Equal(MediaAssetStatus.Processing, asset.STATUS);
    }

    [Fact]
    public void MarkProcessing_FromReady_ThrowsInvalidOperationException()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var asset = MEDIA_ASSET.Create("BunnyStream", "bunny-vid-123", Guid.NewGuid(), true);
        asset.MarkReady("pb-1", 120, "https://cdn/thumb.jpg", clock);

        Assert.Throws<InvalidOperationException>(() => asset.MarkProcessing());
    }

    [Fact]
    public void MarkReady_FromProcessing_SetsPlaybackInfoAndReadyDate()
    {
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var asset = MEDIA_ASSET.Create("BunnyStream", "bunny-vid-123", Guid.NewGuid(), true);
        asset.MarkProcessing();
        asset.MarkReady("pb-123", 360, "https://cdn.example.com/thumb.jpg", clock);

        Assert.Equal(MediaAssetStatus.Ready, asset.STATUS);
        Assert.Equal("pb-123", asset.PLAYBACK_ID);
        Assert.Equal(360, asset.DURATION_SECONDS);
        Assert.Equal("https://cdn.example.com/thumb.jpg", asset.THUMBNAIL_URL);
        Assert.Equal(now, asset.READY_AT_UTC);
    }

    [Fact]
    public void MarkFailed_SetsStatusAndErrorMessage()
    {
        var asset = MEDIA_ASSET.Create("BunnyStream", "bunny-vid-123", Guid.NewGuid(), true);
        asset.MarkFailed("Encoding error code 5");

        Assert.Equal(MediaAssetStatus.Failed, asset.STATUS);
        Assert.Equal("Encoding error code 5", asset.ERROR_MESSAGE);
    }
}
