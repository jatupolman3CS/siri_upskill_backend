using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.AddEpisodeAttachment;
using Xunit;

namespace Siri.UnitTests.Catalog;

public sealed class EpisodeAttachmentTests
{
    [Fact]
    public void EpisodeAttachment_Create_SetsPropertiesCorrectly()
    {
        var episodeId = Guid.NewGuid();
        var attachment = EpisodeAttachment.Create(
            episodeId,
            "slides.pdf",
            "attachments/course-1/slides.pdf",
            "application/pdf",
            2048500);

        Assert.NotEqual(Guid.Empty, attachment.Id);
        Assert.Equal(episodeId, attachment.EpisodeId);
        Assert.Equal("slides.pdf", attachment.FileName);
        Assert.Equal("attachments/course-1/slides.pdf", attachment.StorageKey);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(2048500, attachment.SizeBytes);
    }

    [Theory]
    [InlineData("", "storage/key", "application/pdf", 100, false)]
    [InlineData("file.pdf", "", "application/pdf", 100, false)]
    [InlineData("file.pdf", "storage/key", "", 100, false)]
    [InlineData("file.pdf", "storage/key", "application/pdf", 0, false)]
    [InlineData("file.pdf", "storage/key", "application/pdf", -5, false)]
    [InlineData("file.pdf", "storage/key", "application/pdf", 1024, true)]
    public void AddEpisodeAttachmentValidator_ValidatesInputs(
        string fileName,
        string storageKey,
        string contentType,
        long sizeBytes,
        bool expectedValid)
    {
        var validator = new AddEpisodeAttachmentValidator();
        var command = new AddEpisodeAttachmentCommand(fileName, storageKey, contentType, sizeBytes);
        var result = validator.Validate(command);

        Assert.Equal(expectedValid, result.IsValid);
    }
}
