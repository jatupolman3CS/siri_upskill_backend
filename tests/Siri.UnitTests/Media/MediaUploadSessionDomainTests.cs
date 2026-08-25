using Siri.Modules.Media.Domain;
using Xunit;

namespace Siri.UnitTests.Media;

public sealed class MediaUploadSessionDomainTests
{
    [Fact]
    public void Create_WithValidArgs_InitializesPendingSession()
    {
        var mediaAssetId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        var session = MEDIA_UPLOAD_SESSION.Create(mediaAssetId, "https://upload.bunny.net/tus/123", expiresAt);

        Assert.NotEqual(Guid.Empty, session.MEDIA_UPLOAD_SESSION_ID);
        Assert.Equal(mediaAssetId, session.MEDIA_ASSET_ID);
        Assert.Equal("https://upload.bunny.net/tus/123", session.UPLOAD_URL);
        Assert.Equal(expiresAt, session.EXPIRES_AT_UTC);
        Assert.Equal(MediaUploadSessionStatus.Pending, session.STATUS);
    }

    [Fact]
    public void Complete_FromPending_TransitionsToCompleted()
    {
        var session = MEDIA_UPLOAD_SESSION.Create(Guid.NewGuid(), "https://upload.bunny.net/tus/123", DateTime.UtcNow.AddMinutes(30));
        session.Complete();

        Assert.Equal(MediaUploadSessionStatus.Completed, session.STATUS);
    }

    [Fact]
    public void MarkExpired_FromPending_TransitionsToExpired()
    {
        var session = MEDIA_UPLOAD_SESSION.Create(Guid.NewGuid(), "https://upload.bunny.net/tus/123", DateTime.UtcNow.AddMinutes(30));
        session.MarkExpired();

        Assert.Equal(MediaUploadSessionStatus.Expired, session.STATUS);
    }
}
