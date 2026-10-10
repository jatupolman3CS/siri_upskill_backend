using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Domain;
using Xunit;

namespace Siri.UnitTests.Catalog;

public sealed class EpisodeAttachmentTests
{
    [Fact]
    public void EpisodeAttachment_Create_SetsPropertiesCorrectly()
    {
        var episodeId = Guid.NewGuid();
        var attachment = EPISODE_ATTACHMENT.Create(
            episodeId,
            "slides.pdf",
            "teaching-materials/courses/c/episodes/e/a.pdf",
            "application/pdf",
            2048500);

        Assert.NotEqual(Guid.Empty, attachment.Id);
        Assert.Equal(episodeId, attachment.EpisodeId);
        Assert.Equal("slides.pdf", attachment.FileName);
        Assert.Equal("teaching-materials/courses/c/episodes/e/a.pdf", attachment.StorageKey);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(2048500, attachment.SizeBytes);
    }

    [Theory]
    [InlineData("", "k", "application/pdf", 100)]
    [InlineData("file.pdf", "", "application/pdf", 100)]
    [InlineData("file.pdf", "k", "", 100)]
    public void EpisodeAttachment_Create_RejectsBlankTextFields(string fileName, string storageKey, string contentType, long size)
    {
        Assert.ThrowsAny<ArgumentException>(() => EPISODE_ATTACHMENT.Create(Guid.NewGuid(), fileName, storageKey, contentType, size));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void EpisodeAttachment_Create_RejectsNonPositiveSize(long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EPISODE_ATTACHMENT.Create(Guid.NewGuid(), "a.pdf", "k", "application/pdf", size));
    }

    [Fact]
    public void LiveSessionAttachment_Create_SetsPropertiesCorrectly()
    {
        var sessionId = Guid.NewGuid();
        var attachment = LIVE_SESSION_ATTACHMENT.Create(
            sessionId,
            "  handout.pdf ",
            "teaching-materials/courses/c/live-sessions/s/a.pdf",
            "application/pdf",
            1024);

        Assert.NotEqual(Guid.Empty, attachment.Id);
        Assert.Equal(sessionId, attachment.SessionId);
        Assert.Equal("handout.pdf", attachment.FileName);
        Assert.Equal("teaching-materials/courses/c/live-sessions/s/a.pdf", attachment.StorageKey);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(1024, attachment.SizeBytes);
    }

    [Fact]
    public void LiveSessionAttachment_Create_RejectsEmptySessionId()
    {
        Assert.Throws<ArgumentException>(() => LIVE_SESSION_ATTACHMENT.Create(Guid.Empty, "a.pdf", "k", "application/pdf", 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LiveSessionAttachment_Create_RejectsNonPositiveSize(long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LIVE_SESSION_ATTACHMENT.Create(Guid.NewGuid(), "a.pdf", "k", "application/pdf", size));
    }

    [Fact]
    public void AttachmentOptions_Defaults_AreSafe()
    {
        var options = new EpisodeAttachmentOptions();

        Assert.Equal(50L * 1024 * 1024, options.MaxFileSizeBytes);
        Assert.True(options.MaxFileSizeBytes <= EpisodeAttachmentOptions.HardMaxFileSizeBytes);
        Assert.InRange(options.DownloadUrlTtlSeconds, 60, 900);
        Assert.True(EpisodeAttachmentOptions.MaxUploadRequestBodyBytes > EpisodeAttachmentOptions.HardMaxFileSizeBytes);
    }
}
